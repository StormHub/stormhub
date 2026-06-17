# Shopping Agent Orchestrator (TypeScript)

A small, self-contained demo of the **Orchestrator pattern** for multi-agent
systems — themed as a shopping assistant. Instead of putting the LLM in a loop
(decide → act → observe → repeat), the orchestrator runs three phases with
**exactly two LLM calls**:

1. **Route** — *LLM call 1* (`temperature=0`): the model only *picks tools*. It does not answer.
2. **Execute** — *no LLM*: plain TypeScript runs the selected agents.
3. **Synthesize** — *LLM call 2* (`temperature=0.7`): the model turns structured results into prose.

```
query ──▶ [ROUTE: LLM #1] ──▶ [EXECUTE: agents, no LLM] ──▶ [SYNTHESIZE: LLM #2] ──▶ answer
```

The router chooses one of three execution modes:

| Mode | When | Example |
|---|---|---|
| **single** | one agent answers | "search for running shoes" |
| **parallel** | several independent agents | "price, rating and stock for the iPhone 15?" |
| **sequential** | later step depends on an earlier one (`plan_execution`) | "find a laptop under $1000, check it's in stock, then order it" |

## Exposed over the AG-UI protocol

The agent is served over the **[AG-UI protocol](https://github.com/ag-ui-protocol/ag-ui)**
(`@ag-ui/core` + `@ag-ui/encoder`) as a streaming **SSE** endpoint — not plain JSON.
A single run's event stream maps directly onto the three phases:

| Phase | AG-UI events |
|---|---|
| start | `RUN_STARTED` |
| **execute** (each agent = a tool call) | `TOOL_CALL_START` → `TOOL_CALL_ARGS` → `TOOL_CALL_END` → `TOOL_CALL_RESULT` |
| **synthesize** (streamed answer) | `TEXT_MESSAGE_START` → `TEXT_MESSAGE_CONTENT`… → `TEXT_MESSAGE_END` |
| finish | `RUN_FINISHED` (or `RUN_ERROR`) |

The endpoint accepts an AG-UI `RunAgentInput` (validated with `RunAgentInputSchema`)
and the query is read from the last user message. It works with any AG-UI client
(e.g. `@ag-ui/client`'s `HttpAgent`).

```
POST /        AG-UI run  (text/event-stream)
GET  /health  liveness
```

### Attribution

This is a TypeScript port + shopping re-theme of the pattern described in
**"Beyond the Agentic Loop: The Orchestrator Pattern for Multi-Agent Systems"**
by **Amogh Ubale** (Stackademic, May 29 2026):
<https://stackademic.com/blog/beyond-the-agentic-loop-the-orchestrator-pattern-for-multi-agent-systems>
(That article in turn credits a prior Medium piece by `akanksha.lonkar25`.) The
original is Python + OpenAI with generic `data/analytics/config` agents; this
repo keeps the OpenAI SDK and re-themes the agents around e-commerce.

## Agents

Registered manually in [`src/server/orchestrator/registry.ts`](src/server/orchestrator/registry.ts)
— plain in-process TypeScript, **no Redis/DB/HTTP self-registration** so it runs
with zero infrastructure and is easy to test.

| Skill name | Agent | Purpose |
|---|---|---|
| `catalog_agent__list_categories` | Catalog | List categories (for broad "what do you have?") |
| `catalog_agent__search_products` | Catalog | Search products by keyword/category / max price |
| `inventory_agent__check_stock` | Inventory | Stock + availability |
| `pricing_agent__get_deals` | Pricing | Price + active promotions |
| `reviews_agent__get_reviews` | Reviews | Rating + review highlights |
| `order_agent__place_order` | Order | Place an order (depends on a product) |

Broad questions ("what do you have?") route to `list_categories` so the assistant
offers categories to explore, rather than dumping the whole catalog; a follow-up
("show me laptops") then drills in via `search_products`.

Plus the `plan_execution` **meta-tool** — a *signal*, not an agent. When the
router picks it, the orchestrator switches to sequential mode.

> **Deviation from the original:** the article leaves sequential context-passing
> unspecified. Here `plan_execution` returns an ordered `steps` array, and each
> agent's `execute(args, context)` receives the accumulated prior results — so
> the order step can resolve the product the catalog/inventory step found.

All shopping data is **mocked in-memory** ([`src/server/data/catalog.ts`](src/server/data/catalog.ts))
and deterministic; order placement is faked.

## Setup

```bash
pnpm install
cp .env.example .env      # then add your OPENAI_API_KEY
pnpm dev                  # server on http://localhost:3000
```

To use an **OpenAI-compatible endpoint** (Azure OpenAI, a local model server such
as Ollama/vLLM, or a proxy), set `OPENAI_BASE_URL` in `.env` and point
`ROUTER_MODEL` / `SYNTH_MODEL` at a model that endpoint serves. Leave
`OPENAI_BASE_URL` blank to use OpenAI directly.

## Try it

The endpoint streams SSE, so use `curl -N`. The body is an AG-UI `RunAgentInput`;
the query goes in the last user message.

```bash
# sequential (search -> check stock -> order)
curl -N -X POST localhost:3000/ -H 'content-type: application/json' -d '{
  "threadId":"t1","runId":"r1","state":{},
  "messages":[{"id":"m1","role":"user",
    "content":"find a laptop under $1000, make sure it is in stock, then order it"}],
  "tools":[],"context":[]
}'

# parallel — content: "what is the price, rating and availability of the iPhone 15?"
# single   — content: "search for running shoes"
```

You'll see the SSE event stream: `RUN_STARTED`, the `TOOL_CALL_*` events for each
agent that ran, the streamed `TEXT_MESSAGE_CONTENT` answer, then `RUN_FINISHED`.

### Or use the bundled AG-UI chat client

`src/client/` is an interactive console chat built on `@ag-ui/client`'s `HttpAgent`.
With the server running:

```bash
pnpm client
```

Type requests at the `you ▸` prompt and watch each run stream — the tool calls and
the synthesized answer. One thread is reused for the session; type `exit` to quit.
Point it elsewhere with `SERVER_URL` (default `http://localhost:3000/`).

## Example prompts — single vs parallel vs sequential

This is the heart of the orchestrator pattern: the router reads each request and
decides *how* the agents should run.

**single** — one agent answers:

| Prompt | Routes to |
|---|---|
| `what do you have?` | `catalog_agent__list_categories` |
| `search for running shoes` | `catalog_agent__search_products` |

**parallel** — the facts are independent, so the router picks several tools and the
orchestrator runs them **at once**, in one round:

| Prompt | Routes to (concurrently) |
|---|---|
| `what's the price, rating and availability of the iPhone 15?` | `pricing_agent__get_deals` + `reviews_agent__get_reviews` + `inventory_agent__check_stock` |
| `for the Sony WH-1000XM5, show price, reviews, and whether it's in stock` | `pricing_agent__get_deals` + `reviews_agent__get_reviews` + `inventory_agent__check_stock` |

**sequential** — a later step needs an earlier step's result, so the router emits
`plan_execution` and the orchestrator runs the steps **in order**, passing results
forward:

| Prompt | Routes to (in order) |
|---|---|
| `find a laptop under $1000, make sure it's in stock, then order it` | `catalog_agent__search_products` → `inventory_agent__check_stock` → `order_agent__place_order` |
| `find the cheapest phone and order it` | `catalog_agent__search_products` → `order_agent__place_order` |

> **The contrast in one line:** *"price, rating & stock for X"* is **parallel** — three
> independent lookups with no dependency between them. *"find X, then order it"* is
> **sequential** — the order step can only run once the search has produced a product.
> Same agents, same data; the **shape** of the run is what the router decides.

## Scripts

- `pnpm dev` — start the server with reload (tsx watch)
- `pnpm start` — start the server once
- `pnpm typecheck` — `tsc --noEmit`

## Layout

```
src/client/index.ts     interactive AG-UI chat client (HttpAgent) — `pnpm client`
src/server/
  index.ts              bootstrap (loads .env, validates config, starts server)
  config.ts             zod-validated env (loadConfig / getConfig)
  server.ts             Express app: POST / (AG-UI SSE) + GET /health
  agui/agent.ts         runOrchestratorAgent() — maps the run onto AG-UI events
  llm/openai.ts         getOpenAIClient() factory
  orchestrator/
    types.ts            shared types
    registry.ts         agent registry + OpenAI tool definitions + plan_execution
    router.ts           route()           - LLM call 1
    executor.ts         executeStream()   - single / parallel / sequential (no LLM)
    synthesizer.ts      synthesizeStream() - LLM call 2 (streamed)
  agents/               catalog, inventory, pricing, reviews, order
  lib/resolve-product.ts  shared product-resolution helper used by agents
  data/catalog.ts       mock catalog / stock / deals / reviews
```
