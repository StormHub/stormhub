---
title: Building Autonomous Agent Coding Harness
description: Autonomous agent coding harness for Claude Code.
date: 2026-04-11
tags: [ "AI", "Agent", 'Harness' ]
---

# {{title}}

*{{date | readableDate }}*

This is a personal experiment in autonomous coding [source](https://github.com/StormHub/Agents.Code), built with the [Claude Agent SDK](https://platform.claude.com/docs/en/agent-sdk/overview). It takes a spec (markdown or text) and builds a full-stack application using three specialized agents, as described in this [Anthropic post](https://www.anthropic.com/engineering/harness-design-long-running-apps).

## Requirement
- Build a full-stack application (Next.js + .NET) weather chat. 
- I have manually created an "ideal target solution" [reference implementation](https://github.com/StormHub/Agents.Resources)

## Why This Project Is Hard
While building a weather chat app sounds straightforward, this implementation intentionally introduces architectural challenges that test whether coding agents can work with unfamiliar, cutting-edge libraries — or whether they fall back to well-known patterns:

- **Backend (Agent Construction & Local LLM Integration):** The .NET API utilizes the [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) and exposes the agent via the relatively new [AG-UI](https://docs.ag-ui.com/introduction) protocol. A key challenge lies in the underlying [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) pipeline: coding agents must understand how to connect a local Ollama server, correctly register it as an `IChatClient`, configure the agent with tools, and seamlessly wire everything into the .NET dependency injection container.

- **Schema-Driven UI Rendering (The Catalyst):** To achieve the visual "Generative UI" component, the application utilizes [@vercel-labs/json-render](https://github.com/vercel-labs/json-render). This introduces a profound layer of abstraction. Rather than passing generic data to props, coding agents must grasp an indirect, specification-based rendering model. The frontend strictly expects tool outputs to be converted into a structured UI spec tree (e.g., `Container` -> `WeatherCard` -> `ForecastGrid`), mapped dynamically to concrete React components via a component catalog. 

- **Full-Stack Tool Coupling & Protocol Bridging:** Driven by the strict schema requirements of the UI, tool execution becomes a highly coupled, full-stack concern. The backend emits raw AG-UI Server-Sent Events (SSE), which the Next.js server must manually parse and map to the Vercel [AI SDK](https://github.com/vercel/ai) 'UIMessage' types. Crucially, because the AG-UI protocol exposes tool execution results directly to the client stream as JSON payloads, coding agents must explicitly co-design the C# backend tool call result types to satisfy the frontend's schema-driven expectations.

- **Custom Generative UI Transport & State:** Because these tightly-coupled tool outputs stream directly to the client, standard AI SDK hooks aren't enough out-of-the-box. The frontend requires configuring `useChat` with a custom `DefaultChatTransport`. Agents must design the UI interface such that the incoming JSON payloads seamlessly inject complex parts into the `ChatMessage` state. They must deeply understand multi-part message trees—accurately inspecting `part.type` and `part.state === "output-available"` to interrupt typical text rendering and conditionally mount the generated JSON UI spec.

## First Round Result
- [Feature requirement file](https://github.com/StormHub/Agents.Code/results/round-1/features.md) only — intentionally instructed to use simulated/mock weather data to reduce complexity
- [Result output](https://github.com/StormHub/Agents.Code/results/round-1/output)

### Gap Analysis

| Dimension | Reference (Target) | Generated (Round 1) |
|---|---|---|
| **.NET version** | .NET 10 | .NET 8 |
| **Backend framework** | Microsoft Agent Framework (`Microsoft.Agents.AI`) | Plain ASP.NET Core MVC |
| **Streaming protocol** | AG-UI via SSE | Standard JSON REST |
| **LLM integration** | Ollama via `OllamaSharp` + `IChatClient` DI | None — rule-based string matching |
| **Frontend AI SDK** | `@ai-sdk/react` `useChat` + `DefaultChatTransport` | Raw `fetch()` + `useState` |
| **UI rendering** | `@json-render` (schema-driven spec tree) | Direct hardcoded React components |

Every architectural constraint specified in the feature requirements — AG-UI, Microsoft Agent Framework, Ollama, json-render — was ignored. The agents built a conventional CRUD-style app instead.

**What it got right:** The app is functional end-to-end with good visual design (glassmorphic cards, dynamic backgrounds, custom SVG icons), responsive layout, and clean code structure. About 7 of 16 features work partially or fully.

**What it missed:** No SSE streaming, no LLM tool calling (just regex location extraction), no schema-driven UI rendering, no AI SDK hooks. The `ai` npm package was even installed but never imported.

**Takeaway:** Given only a feature spec, coding agents gravitate toward familiar patterns from training data. The novel integration requirements (AG-UI, json-render, Agent Framework) — which are the architecturally interesting parts — were completely bypassed in favor of well-known alternatives.

## Second Round Result
- Enhanced [feature requirements](https://github.com/StormHub/Agents.Code/results/round-2/features.md) with explicit architectural instructions — specifying `MapAGUI`, `ChatClientAgent`, `defineCatalog`/`defineRegistry`, `useChat` with transport, etc.
- After round 1's results, custom skills created for json-render and Microsoft Agent Framework, and installed official Vercel Next.js and AI SDK skills to give agents better guidance
- [Result output](https://github.com/StormHub/Agents.Code/results/round-2/output)

### Gap Analysis

| Dimension | Reference (Target) | Generated (Round 2) |
|---|---|---|
| **.NET version** | .NET 10 | .NET 10 |
| **Backend framework** | Microsoft Agent Framework (`MapAGUI`) | Packages installed but **not used** — plain REST API |
| **Streaming protocol** | AG-UI via SSE | Standard JSON REST |
| **LLM integration** | Ollama via `OllamaSharp` + `IChatClient` | Package installed, only checks if Ollama is running — **never calls it** |
| **Frontend AI SDK** | `@ai-sdk/react` `useChat` + `DefaultChatTransport` | Package installed but uses raw `fetch()` |
| **UI rendering** | `@json-render/react` (real package) | **Fake shim** — hand-written `json-render-compat.ts` reimplements `defineCatalog`/`defineRegistry` as simple wrappers |

**Progress from round 1:** The agents now *acknowledge* the required technologies — correct .NET version, right NuGet packages installed, catalog/registry file structure present. The feature requirements with explicit API names clearly helped.

**What's still wrong:** The acknowledgment is superficial. The agents installed `Microsoft.Agents.AI` and `OllamaSharp` but never called `MapAGUI()` or created a `ChatClientAgent`. Instead of installing `@json-render/react`, they wrote a 40-line compatibility shim that mimics the API surface but does nothing — the `<Renderer>` component from json-render is never used. The backend is still hardcoded pattern matching over 6 cities with no LLM.

**Takeaway:** Adding skills and explicit architectural instructions moved agents from "completely ignore" to "install the packages and create the right file names." But the actual wiring — the hard part — was still substituted with familiar patterns. The agents created a *cargo cult* of the architecture: the right shape, with none of the substance.

## Conclusion

The progression across rounds tells a clear story. Round 1 completely ignored the architectural requirements. Round 2 acknowledged them superficially — installing the right packages, creating files with the right names — but never actually wired anything up. The hand-written json-render shim and the unused NuGet packages are the most telling evidence.

None of this is entirely surprising. These are integration challenges that even experienced engineers would need to research and iterate on — connecting unfamiliar frameworks across a full-stack boundary is genuinely hard. The deeper issue is that even with upfront planning enforced (preventing agents from "one-shotting" the app), intrinsic technical challenges in the implementation details cause coding agents to silently fall back to what they know.

What these experiments suggest is that producing quality implementations with coding agents requires highly detailed, step-by-step plans — not just feature specs or architectural diagrams, but concrete wiring instructions that leave little room for substitution. Simply adding skills as supplementary context does not bridge the gap when the core integration patterns are unfamiliar to the model.

## Next Steps

The experiments above point to a clear gap: the planning agent produces plans that are too high-level for the coding agent to follow faithfully when unfamiliar technologies are involved. The next iteration of the harness will focus on two changes:

1. **Interactive upfront planning:** Rather than generating a plan in one shot and handing it off, the planning agent will produce a detailed, step-by-step implementation plan that can be reviewed and refined before any code is written. Each step should be concrete enough that the coding agent knows exactly which API to call, which package to import, and how to wire it — leaving no room for silent substitution.

2. **Step-by-step execution with verification:** Instead of letting the coding agent execute the entire plan autonomously, the harness will execute one step at a time, verifying the output of each step (builds, tests, correct imports) before proceeding to the next. This catches drift early — if the agent installs a package but doesn't use it, or writes a shim instead of using the real library, the verification step surfaces the problem immediately rather than letting it compound.

This follows the approach outlined in the [autonomous coding quickstart](https://github.com/anthropics/claude-quickstarts/tree/main/autonomous-coding), adapted to the multi-agent harness architecture described in this project.