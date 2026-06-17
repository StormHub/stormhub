---
title: Beyond the Agentic Loop, in TypeScript
description: Beyond the Agentic Loop, in TypeScript building a shopping agent with the Orchestrator pattern.
date: 2026-06-17
tags: [ "AI", "Agent", "TypeScript" ]
---

# {{title}}

*{{date | readableDate }}*

This post is a TypeScript implementation of the pattern described in [**"Beyond the Agentic Loop: The Orchestrator Pattern for Multi-Agent Systems"**](https://stackademic.com/blog/beyond-the-agentic-loop-the-orchestrator-pattern-for-multi-agent-systems) by **Amogh Ubale** (Stackademic). The original is Python with generic agents; here we keep the idea intact and re-theme it as a **shopping assistant** so the three execution modes have something concrete to chew on. All the design credit goes to that article — go read it first.

## The problem: the LLM as a `while` loop

The default way to build a multi-agent system is the **agentic loop**: you hand the
model a bag of tools and let it drive.

```
think → call a tool → observe the result → think again → call another tool → …
```

The LLM is both the brain *and* the control flow. That's wonderfully flexible, and
it's the right tool when the task is open-ended and you genuinely don't know the
steps in advance. But in production it has three nasty properties:

- **Unpredictable shape.** Every "think" step is another LLM round-trip, so a
  three-agent task might take 3 calls or 9 — you don't know until it runs, and latency
  swings with it. (The article clocks a representative three-agent query at ~7 calls
  through the loop; the wall-clock and spend follow, but the unpredictability is the
  part that actually bites.)
- **Non-determinism.** The same question can take a different path each time, which
  makes behavior hard to reason about and hard to trust with side effects — like
  *placing an order*.
- **Poor observability.** "Why did it do that?" means replaying a transcript of
  intermingled reasoning and tool calls. There's no single place where the *plan*
  lives.

If you already know which agents exist and what they do, an open-ended reasoning loop
on every request is more freedom than the job needs.

## The pattern: decide once, execute deterministically

The orchestrator's move is to **separate the decision from the execution**. Instead
of letting the model loop, you make exactly **two** LLM calls with plain,
deterministic code in between:

```
query ──▶ [ROUTE: LLM #1] ──▶ [EXECUTE: agents, no LLM] ──▶ [SYNTHESIZE: LLM #2] ──▶ answer
```

1. **Route** — one LLM call whose *only* job is to pick which agent(s) to run.
2. **Execute** — ordinary application code runs those agents. No LLM here.
3. **Synthesize** — one LLM call turns the structured results into prose.

Two calls, every time, no matter how many agents run. That fixed shape is the whole
point: a plan you can inspect before anything happens, latency that doesn't depend on
the model's mood, and independent work you can fan out. (It's cheaper too — the article
puts the same query at ~2 calls instead of ~7 — but the cost isn't the headline; the
*outcomes* are.)

## 1. The registry: agents are just functions

An agent is a name, a description (for the router), a JSON-Schema for its arguments,
and an `execute` function. Nothing more.

```ts
// src/server/orchestrator/types.ts
export type ExecuteFn = (args: AgentArgs, context: AgentContext) => Promise<AgentResult>;

export interface AgentDefinition {
  agent: string;        // human name, e.g. "Catalog Agent"
  description: string;  // shown to the router LLM so it can choose this tool
  parameters: Record<string, unknown>; // JSON Schema for the args
  execute: ExecuteFn;
}
```

The "registry" is a plain in-process object — agents are **registered by hand**.
There's deliberately no Redis, no database, no HTTP self-registration. That keeps the
whole thing runnable and testable with zero infrastructure.

```ts
// src/server/orchestrator/registry.ts
export const REGISTRY: Record<string, AgentDefinition> = {
  catalog_agent__list_categories: catalogCategoriesAgent,
  catalog_agent__search_products: catalogAgent,
  inventory_agent__check_stock: inventoryAgent,
  pricing_agent__get_deals: pricingAgent,
  reviews_agent__get_reviews: reviewsAgent,
  order_agent__place_order: orderAgent,
};
```

`toolDefinitions()` projects that map into the OpenAI tool format the router sees —
each agent becomes one function tool, plus one **meta-tool** we'll meet shortly.

## 2. Route: the one decision-making LLM call

The router is given a blunt system prompt: *pick tools, do not answer.*

```ts
// src/server/orchestrator/router.ts
const SYSTEM_PROMPT = `You are a query router. Your ONLY job is to decide which tool(s) to call.
Rules:
- If the query needs ONE agent, call that one tool.
- If the query needs MULTIPLE INDEPENDENT agents, call all of them.
- If the query needs steps IN ORDER (a later step depends on an earlier one), call plan_execution and provide the ordered steps.
Do NOT answer the user's question — just pick tools.`;
```

We call the model at `temperature: 0` with `tool_choice: "auto"`, then read its tool
calls back out. The shape of that tool-call list *is* the execution plan — we never
ask the model to "answer," only to choose:

```ts
// src/server/orchestrator/router.ts
export async function route(query: string): Promise<RouteDecision> {
  const response = await getOpenAIClient().chat.completions.create({
    model: getConfig().ROUTER_MODEL,
    temperature: 0,
    tools: toolDefinitions(),
    tool_choice: "auto",
    messages: [
      { role: "system", content: SYSTEM_PROMPT },
      { role: "user", content: query },
    ],
  });

  const toolCalls = response.choices[0]?.message.tool_calls ?? [];

  // plan_execution present -> sequential. Take its ordered steps.
  const planCall = toolCalls.find((c) => c.function.name === PLAN_EXECUTION_TOOL);
  if (planCall) {
    const parsed = safeParseArgs(planCall.function.arguments) as {
      steps?: Array<{ tool: string; args?: AgentArgs; reason?: string }>;
    };
    const steps = (parsed.steps ?? []).map((s) => ({ tool: s.tool, args: s.args ?? {}, reason: s.reason }));
    return { mode: "sequential", steps };
  }

  const steps = toolCalls.map((c) => ({ tool: c.function.name, args: safeParseArgs(c.function.arguments) }));
  return { mode: steps.length > 1 ? "parallel" : "single", steps };
}
```

So the router collapses to three outcomes:

- **one** tool → `single`
- **several** tools → `parallel`
- the **`plan_execution`** meta-tool → `sequential`

## 3. Execute: the heart of the pattern (no LLM)

This is where parallel and sequential actually diverge — and it's pure TypeScript,
no model involved.

```ts
// src/server/orchestrator/executor.ts
export async function* executeStream(mode: Mode, steps: PlanStep[]): AsyncGenerator<ExecEvent, AgentContext> {
  const results: AgentContext = {};

  if (mode === "parallel") {
    for (const step of steps) yield { kind: "agent_start", tool: step.tool, args: step.args };
    const settled = await Promise.all(
      steps.map(async (step) => [step.tool, await runAgent(step, {})] as const),
    );
    for (const [tool, result] of settled) {
      results[tool] = result;
      yield { kind: "agent_result", tool, result };
    }
    return results;
  }

  // single + sequential: ordered; each step sees prior results as context.
  for (const step of steps) {
    yield { kind: "agent_start", tool: step.tool, args: step.args };
    const result = await runAgent(step, results);
    results[step.tool] = result;
    yield { kind: "agent_result", tool: step.tool, result };
  }
  return results;
}
```

Read the two branches side by side:

- **Parallel** is `Promise.all`. The agents are independent, so they all fire at
  once and you pay for the slowest one, not the sum. *"What's the price, rating, and
  stock of the iPhone 15?"* becomes three lookups that have nothing to say to each
  other — run them together.
- **Sequential** is an ordered `for` loop where each step receives the accumulated
  `results` as its `context`. That's how a later agent consumes an earlier one's
  output. *"Find a laptop under $1000, check it's in stock, then order it"* can't be
  parallel — the order step needs the product the search produced.

(The generator `yield`s a small event before and after each agent. That's only so a
transport can show progress; it doesn't change the logic.)

## 4. `plan_execution`: a signal, not an agent

How does the router say "do these in order"? With a meta-tool that runs no code:

```ts
// src/server/orchestrator/registry.ts
export const PLAN_EXECUTION_TOOL = "plan_execution";
// ...its tool schema asks for { reason, steps: [{ tool, args, reason }] }
```

When the router selects `plan_execution`, the orchestrator switches to sequential
mode. The original article treats it purely as a *signal* and leaves the ordering and
data-passing unspecified. This repo makes one deliberate addition so the demo
actually works end-to-end: **`plan_execution` returns the ordered `steps`**, and the
executor threads `results` forward as context. The order agent then resolves the
product the search found (see `resolveTargetProduct` in
`src/server/lib/resolve-product.ts`). That's the difference between a pattern diagram
and a thing you can run.

## 5. Synthesize: the only creative call

Once the agents have produced structured data, a second LLM call turns it into an
answer. This is the only step with any "writing" to do, so it runs warmer and streams
its tokens out.

```ts
// src/server/orchestrator/synthesizer.ts
export async function* synthesizeStream(query: string, results: AgentContext): AsyncGenerator<string> {
  const stream = await getOpenAIClient().chat.completions.create({
    model: getConfig().SYNTH_MODEL,
    temperature: 0.7,
    stream: true,
    messages: [
      { role: "system", content: "Summarize the agent results into a clear, helpful answer." },
      { role: "user", content: `User asked: ${query}\nResults: ${JSON.stringify(results)}` },
    ],
  });
  for await (const chunk of stream) {
    const delta = chunk.choices[0]?.delta?.content;
    if (delta) yield delta;
  }
}
```

## What this buys you

Putting the three phases together, the payoff is exactly the inverse of the loop's
pain points — and these *enablements*, not the price tag, are the real reason to reach
for it:

- **A plan you can trust.** The decision is a single inspectable object — the
  `RouteDecision` — produced *before* any agent runs. You can log it, assert on it,
  gate it, replay it. That's what makes it safe to let an agent actually place an
  order.
- **Debuggability.** The execute phase is deterministic, so a bug there reproduces
  every time instead of hiding in a different transcript on each run.
- **Parallelism for free.** Independent work is a `Promise.all`; you didn't have to
  teach the model to be concurrent.
- **A testable core.** Because the middle phase has no LLM in it, `executeStream` is
  an ordinary async function you can unit-test with a stub registry — no API key, no
  flakiness.
- **Predictable runs** (the boring-but-nice one). Always two LLM calls, whether the
  request touches one agent or five — so latency is something you can put a number on,
  and the bill is lower as a side effect.

## Sample queries → how they route

| Query | Mode | Agents |
|---|---|---|
| `what do you have?` | single | `catalog_agent__list_categories` |
| `what's the price, rating and availability of the iPhone 15?` | parallel | `pricing` + `reviews` + `inventory` (at once) |
| `find a laptop under $1000, make sure it's in stock, then order it` | sequential | `search` → `check stock` → `order` |

Same agents, same data — the router decides the **shape** of the run.

## When the loop still wins

This isn't "orchestrator good, loop bad." The agentic loop is the right tool when the
task is genuinely exploratory: you don't know the steps ahead of time, the toolset is
open-ended, or the agent needs to re-plan mid-flight based on what it discovers. The
orchestrator trades that adaptability for predictability — and it assumes you can
enumerate your agents up front. Note too that the router here is itself a single LLM
call, so a truly novel multi-hop plan it has never seen is out of scope by design.

The article's framing is the one to keep: **loop for exploration, orchestrator for
production.** If you already know your agents and you need bounded latency, parallel
execution, and debuggable runs — ask the model once, execute, synthesize. Two calls,
done.

---


[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2026-06-17/agent-orchestrator)