---
title: Agent with Vercel's Eve Framework
description: Building a Shopping Agent with Vercel's Eve Framework.
date: 2026-06-22
tags: [ "AI", "Agent", "TypeScript" ]
---

# {{title}}

*{{date | readableDate }}*

Vercel recently open-sourced [Eve](https://github.com/vercel/eve), a framework for building durable AI agents. It takes an opinionated, filesystem-first approach: instead of wiring up model loops, tool dispatch, and session persistence yourself, you author a directory of files and Eve handles the rest.

I took it for a spin by building a shopping assistant — an agent that can search a product catalog, check inventory, compare prices, read reviews, and place orders. Here's what I found.

## The Core Separation: Agent vs Channel

Eve draws a hard line between what the agent *is* and how it *communicates*. The agent is the reasoning core — model, tools, instructions. It doesn't know or care how users reach it. A channel is just comms — it handles inbound transport, auth, message format, and delivery for a specific platform.

This means the same agent can simultaneously serve a browser chat widget, a Slack bot, a CLI, and a custom webhook — without any conditional logic in the agent itself. You add surfaces by adding channel files, not by changing agent code.

## Channels: How Users Reach the Agent

In Eve, a **channel** is the edge adapter between a platform and your agent. It normalizes inbound messages, owns the conversation resume handle (`continuationToken`), and decides how responses get delivered back.

The default channel is `eve.ts` — the HTTP session API that the dev TUI, browser clients, and `curl` all talk to. But channels aren't limited to HTTP. Eve ships integrations for Slack, Discord, Teams, Telegram, Twilio (SMS/voice), GitHub, and Linear. You can also write custom channels for any surface (webhooks, WebSockets, internal systems).

Each channel is a file under `agent/channels/`:

```
agent/channels/
├── eve.ts      # HTTP API (always present, even without a file)
├── slack.ts    # Slack DMs, mentions, buttons
└── intake.ts   # Your custom webhook channel
```

The key insight: your agent logic (instructions + tools) stays the same regardless of channel. You write the agent once, then expose it through multiple surfaces by adding channel files. The channel handles platform-specific concerns (auth, message format, delivery) while the agent handles reasoning.

### The Default Eve Channel

The `eve.ts` channel is always present — it provides a session-based HTTP API that the dev TUI, browser clients (`useEveAgent`), and `curl` all use. The key concept is **durable sessions**: you `POST` to create a session, stream events from it via NDJSON, and continue it with a `continuationToken`. Sessions survive server restarts and support reconnection at any event index (`?startIndex=N`). This is fundamentally different from stateless request/response — Eve owns the conversation state server-side.

### How Sessions Are Durable by Default

Eve sessions aren't just "kept in memory" — they're backed by a workflow engine. Under the hood, every turn runs as a durable workflow built on the open-source [Workflow SDK](https://workflow-sdk.dev/). Eve checkpoints progress at each **step** boundary (one model call + its tool calls = one step). If the process crashes mid-turn, it resumes from the last completed step rather than replaying everything.

Locally, this is just files on disk. Run your agent and you'll find a `.workflow-data/` directory:

```
.workflow-data/
├── runs/       # one JSON file per session (workflow state)
├── steps/      # checkpoint per completed step
├── streams/    # event streams (what clients read via /stream)
├── hooks/      # parked continuation tokens (waiting for input)
└── events/     # workflow lifecycle events
```

This means you can:
1. Start a conversation, ask the agent something
2. Kill the server (`Ctrl+C`)
3. Restart it
4. Continue the conversation with the same `sessionId` and `continuationToken`

The session picks up exactly where it left off — including mid-turn recovery if a step was already completed before the crash.

Obviously, local files aren't scalable for production. Eve's durability is pluggable via the [Workflow SDK](https://workflow-sdk.dev/)'s "World" abstraction — the storage/queue/streaming backend. You pick a world package and Eve uses it for all session persistence:

| World | Backend | Use case |
|-------|---------|----------|
| `@workflow/world-local` | Filesystem (`.workflow-data/`) | Local dev (default) |
| `@workflow/world-postgres` | PostgreSQL + graphile-worker | Self-hosted production |
| `@workflow/world-vercel` | Vercel Workflow (managed) | Vercel deployments |
| `@workflow-worlds/redis` | Redis + BullMQ | Self-hosted, high throughput |
| `@workflow-worlds/mongodb` | MongoDB | Self-hosted |
| `@workflow-worlds/turso` | Turso/libSQL (embedded SQLite) | Edge/embedded |

To switch, you set it in `agent.ts`:

```ts
export default defineAgent({
  model: openai("gpt-4o"),
  modelContextWindowTokens: 128_000,
  experimental: {
    workflow: {
      world: "@workflow/world-postgres",
    },
  },
});
```

The World interface has three responsibilities: **Storage** (persisting runs, steps, hooks via an append-only event log), **Queue** (dispatching workflow/step invocations with at-least-once delivery), and **Streams** (real-time event delivery to clients). You can also [build your own](https://workflow-sdk.dev/docs/deploying/building-a-world) if none of the existing options fit.

### Example: Exposing the Agent as AG-UI

To make this concrete — I built a custom channel that exposes the Eve agent over the [AG-UI protocol](https://github.com/ag-ui-protocol/ag-ui) (SSE-based, used by CopilotKit and similar frameworks). The channel translates Eve's internal event stream into AG-UI's event vocabulary:

```ts
// agent/channels/agui.ts
import { defineChannel, POST, type Session } from "eve/channels";
import { EventEncoder } from "@ag-ui/encoder";
import { EventType, type BaseEvent, type RunAgentInput } from "@ag-ui/core";

export default defineChannel({
  routes: [
    POST("/agui", async (req, { send }) => {
      const body = (await req.json()) as RunAgentInput;
      if (!body.threadId) {
        return Response.json({ error: "Missing 'threadId'." }, { status: 400 });
      }

      const { threadId } = body;
      const runId = body.runId ?? randomUUID();
      const messages = body.messages ?? [];

      // AG-UI is stateless per request — clients send full message history.
      // Pass prior messages as context so Eve's agent sees the conversation.
      const lastUserIdx = messages.findLastIndex((m) => m.role === "user");
      const context = messages.slice(0, lastUserIdx).map((m) =>
        `[${m.role}]: ${typeof m.content === "string" ? m.content : JSON.stringify(m.content)}`
      );

      const session: Session = await send(
        { message: messages[lastUserIdx]?.content ?? "", context },
        { auth: null, continuationToken: `agui:${threadId}:${randomUUID()}` },
      );

      // Read Eve's event stream and translate to AG-UI SSE
      const eveStream = await session.getEventStream();
      const encoder = new EventEncoder();
      // ... event mapping loop (see full source)
    }),
  ],
});
```

The event mapping is mostly mechanical — `actions.requested` → `TOOL_CALL_START`/`ARGS`/`END`, `action.result` → `TOOL_CALL_RESULT`, `message.appended` → `TEXT_MESSAGE_CONTENT`, `turn.completed` → `RUN_FINISHED`. But there were a few non-obvious gotchas:

**1. Eve sessions are durable; AG-UI runs are not.** Eve's event stream never closes after a turn — it waits for the next message. You must close the response stream yourself after emitting `RUN_FINISHED`. If you don't, the client hangs forever waiting for more data.

**2. Eve emits both `turn.completed` and `session.waiting` at turn boundaries.** If you naively emit `RUN_FINISHED` for both, the AG-UI client throws: *"Cannot send event type 'RUN_FINISHED': The run has already finished."* Guard with a flag and only emit once.

**3. AG-UI is stateless; Eve is stateful.** Each AG-UI request carries the full message history. Since Eve's `send()` creates/continues sessions via `continuationToken`, you need a fresh token per request (otherwise Eve tries to continue a stale session). The conversation history goes through `context` so the agent sees prior turns.

**4. Eve actions are typed unions.** `actions.requested` contains a `RuntimeActionRequest[]` that can be `tool-call`, `subagent-call`, or `load-skill`. You need to filter for `action.kind === "tool-call"` and use `action.toolName` / `action.callId` (not `.name` / `.id` which don't exist on the type).

Same agent, same tools, same instructions — but now it speaks AG-UI over SSE at `POST /agui`. You could have the Eve HTTP channel, a Slack channel, and this AG-UI channel all running simultaneously, each talking to the same underlying agent.

## The Developer Experience

Run `pnpm dev` (or `npx eve dev`) and you get an interactive terminal UI that speaks the eve channel protocol locally:

```
☰eve  shopping-agent-orchestrator

> show me laptops under $1000

✓ search_products  query="laptop" maxPrice=1000 → 1 result
✓ get_pricing  product="Dell XPS 13" → $899.10 (Summer Sale)
✓ check_stock  product="Dell XPS 13" → 12 units

I found the Dell XPS 13 for $899.10 (10% off with the Summer Sale).
It's in stock with 12 units available. Would you like to place an order?
```

The TUI shows:
- Tool calls as they happen (collapsed to one-line summaries)
- Streaming text as the model generates it
- Token usage stats
- Slash commands (`/model` to switch models, `/new` for a fresh session)

File changes trigger a hot rebuild — edit a tool, and it's live on the next message.

## The Gotcha: Custom Models Need `modelContextWindowTokens`

If you're using a non-gateway model (custom `baseURL`, direct provider), Eve's build will fail with a cryptic error about compaction metadata. You need to explicitly tell it the context window size:

```ts
// agent/agent.ts
import { createOpenAI } from "@ai-sdk/openai";
import { defineAgent } from "eve";

const openai = createOpenAI({
  apiKey: process.env.OPENAI_API_KEY!,
  baseURL: process.env.OPENAI_BASE_URL, // custom endpoint
});

export default defineAgent({
  model: openai("gpt-4o"),
  modelContextWindowTokens: 128_000, // required for direct providers
});
```

**What is `modelContextWindowTokens`?** Eve has a built-in "compaction" system that prevents long conversations from overflowing the model's context window. When the conversation reaches ~90% of the window, Eve automatically summarizes older turns into a compressed form so the session can keep going indefinitely. To do this, Eve needs to know how big the window is. Gateway models (like `"openai/gpt-4o"`) have this metadata in Vercel's catalog. But when you bring your own provider via `createOpenAI()`, Eve has no way to look it up — so you tell it explicitly.

Without this field, the build fails at compile time rather than silently skipping compaction at runtime. The error message (`"does not have known AI Gateway context window metadata"`) doesn't make the fix obvious.

## Key Takeaways

- **Zero orchestration code.** I didn't write a single line of routing, tool dispatch, or streaming logic. The model handles multi-step reasoning natively — search → check stock → order — guided by the markdown instructions.
- **The filesystem convention removes boilerplate.** Adding a new capability is "create a file." No registration, no imports to wire up.
- **Durable sessions out of the box.** Multi-turn conversations, reconnection, human-in-the-loop approval — it's all built in.
- **The dev TUI is genuinely useful.** Seeing tool calls execute in real-time while developing makes the feedback loop fast.

---

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2026-06-22)