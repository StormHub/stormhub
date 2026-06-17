import { randomUUID } from "node:crypto";
import {
  EventType,
  type BaseEvent,
  type Message,
  type RunAgentInput,
  type RunStartedEvent,
  type RunFinishedEvent,
  type RunErrorEvent,
  type TextMessageStartEvent,
  type TextMessageContentEvent,
  type TextMessageEndEvent,
  type ToolCallStartEvent,
  type ToolCallArgsEvent,
  type ToolCallEndEvent,
  type ToolCallResultEvent,
} from "@ag-ui/core";

import { executeStream } from "../orchestrator/executor.js";
import { route } from "../orchestrator/router.js";
import { synthesizeStream } from "../orchestrator/synthesizer.js";
import type { AgentContext } from "../orchestrator/types.js";

/** Extract the query from the latest user message. */
function lastUserQuery(messages: Message[]): string {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    if (m?.role === "user") {
      return typeof m.content === "string" ? m.content : "";
    }
  }
  return "";
}

/**
 * Run the orchestrator as an AG-UI agent, emitting the protocol event stream:
 *   RUN_STARTED
 *     -> per agent: TOOL_CALL_START / _ARGS / _END / _RESULT   (the execute phase)
 *     -> TEXT_MESSAGE_START / _CONTENT* / _END                  (the streamed synthesis)
 *   RUN_FINISHED   (or RUN_ERROR on failure)
 *
 * Maps route -> execute -> synthesize onto a single AG-UI run.
 */
export async function* runOrchestratorAgent(input: RunAgentInput): AsyncGenerator<BaseEvent> {
  const { threadId, runId } = input;
  yield { type: EventType.RUN_STARTED, threadId, runId } satisfies RunStartedEvent;

  try {
    const query = lastUserQuery(input.messages);

    // Phase 1: ROUTE (LLM call 1)
    const decision = await route(query);

    // Phase 2: EXECUTE (no LLM) — each agent surfaces as a tool call.
    const parentMessageId = randomUUID();
    const toolCallIds = new Map<string, string>();
    const results: AgentContext = {};

    for await (const ev of executeStream(decision.mode, decision.steps)) {
      if (ev.kind === "agent_start") {
        const toolCallId = randomUUID();
        toolCallIds.set(ev.tool, toolCallId);
        yield {
          type: EventType.TOOL_CALL_START,
          toolCallId,
          toolCallName: ev.tool,
          parentMessageId,
        } satisfies ToolCallStartEvent;
        yield {
          type: EventType.TOOL_CALL_ARGS,
          toolCallId,
          delta: JSON.stringify(ev.args),
        } satisfies ToolCallArgsEvent;
        yield { type: EventType.TOOL_CALL_END, toolCallId } satisfies ToolCallEndEvent;
      } else {
        results[ev.tool] = ev.result;
        const toolCallId = toolCallIds.get(ev.tool) ?? randomUUID();
        yield {
          type: EventType.TOOL_CALL_RESULT,
          messageId: randomUUID(),
          toolCallId,
          content: JSON.stringify(ev.result),
          role: "tool",
        } satisfies ToolCallResultEvent;
      }
    }

    // Phase 3: SYNTHESIZE (LLM call 2) — streamed as an assistant text message.
    const messageId = randomUUID();
    yield { type: EventType.TEXT_MESSAGE_START, messageId, role: "assistant" } satisfies TextMessageStartEvent;
    for await (const delta of synthesizeStream(query, results)) {
      yield { type: EventType.TEXT_MESSAGE_CONTENT, messageId, delta } satisfies TextMessageContentEvent;
    }
    yield { type: EventType.TEXT_MESSAGE_END, messageId } satisfies TextMessageEndEvent;

    yield { type: EventType.RUN_FINISHED, threadId, runId } satisfies RunFinishedEvent;
  } catch (err) {
    const message = err instanceof Error ? err.message : "Unknown error";
    yield { type: EventType.RUN_ERROR, message } satisfies RunErrorEvent;
  }
}
