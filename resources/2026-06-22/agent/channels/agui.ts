import { randomUUID } from "node:crypto";
import { defineChannel, POST, type Session } from "eve/channels";
import { EventEncoder } from "@ag-ui/encoder";
import {
  EventType,
  type BaseEvent,
  type RunAgentInput,
} from "@ag-ui/core";

/**
 * AG-UI channel — exposes the Eve agent over the AG-UI protocol.
 *
 * POST /agui — accepts a RunAgentInput body, returns an SSE stream of AG-UI events.
 *
 * Maps Eve session events (actions.requested, action.result, message.appended, etc.)
 * to the AG-UI event vocabulary (RUN_STARTED, TOOL_CALL_*, TEXT_MESSAGE_*, RUN_FINISHED).
 */
export default defineChannel({
  routes: [
    POST("/agui", async (req, { send }) => {
      const body = (await req.json()) as RunAgentInput;

      if (!body.threadId) {
        return Response.json(
          { error: "Missing required field 'threadId'.", ok: false },
          { status: 400 },
        );
      }

      const threadId = body.threadId;
      const runId = body.runId ?? randomUUID();
      const encoder = new EventEncoder({
        accept: req.headers.get("accept") ?? undefined,
      });

      const messages = body.messages ?? [];

      // Build conversation context from prior messages (all except the last user message)
      // so Eve's agent sees the full history even though each AG-UI request creates a fresh session.
      const lastUserIdx = messages.findLastIndex((m) => m.role === "user");
      const priorMessages = messages.slice(0, lastUserIdx >= 0 ? lastUserIdx : messages.length);
      const context: string[] = priorMessages.map((m) => {
        const content = typeof m.content === "string" ? m.content : JSON.stringify(m.content);
        return `[${m.role}]: ${content}`;
      });

      const lastUserMessage = lastUserIdx >= 0 ? messages[lastUserIdx] : undefined;
      const message =
        typeof lastUserMessage?.content === "string"
          ? lastUserMessage.content
          : lastUserMessage
            ? JSON.stringify(lastUserMessage.content)
            : "";

      // Send the message to Eve with conversation history as context
      const session: Session = await send(
        { message, context: context.length > 0 ? context : undefined },
        {
          auth: null,
          continuationToken: `agui:${threadId}:${randomUUID()}`,
        },
      );

      // Get the event stream from the session
      const eveStream = await session.getEventStream();

      // Track state for mapping Eve events → AG-UI events
      const messageId = randomUUID();
      let textStarted = false;
      let runFinished = false;
      let toolCallCounter = 0;
      const toolCallIdMap = new Map<string, string>();

      const aguiStream = new ReadableStream<Uint8Array>({
        async start(controller) {
          const textEncoder = new TextEncoder();

          function emit(event: BaseEvent) {
            const encoded = encoder.encodeSSE(event);
            controller.enqueue(textEncoder.encode(encoded));
          }

          // Emit RUN_STARTED
          emit({ type: EventType.RUN_STARTED, threadId, runId });

          const reader = eveStream.getReader();
          try {
            while (true) {
              const { done, value } = await reader.read();
              if (done) break;

              const event = value;
              switch (event.type) {
                case "actions.requested": {
                  // Each action maps to TOOL_CALL_START + TOOL_CALL_ARGS + TOOL_CALL_END
                  for (const action of event.data.actions) {
                    if (action.kind !== "tool-call") continue;
                    const toolCallId = `tc_${toolCallCounter++}`;
                    toolCallIdMap.set(action.callId, toolCallId);

                    emit({
                      type: EventType.TOOL_CALL_START,
                      toolCallId,
                      toolCallName: action.toolName,
                      parentMessageId: messageId,
                    });
                    emit({
                      type: EventType.TOOL_CALL_ARGS,
                      toolCallId,
                      delta: JSON.stringify(action.input ?? {}),
                    });
                    emit({
                      type: EventType.TOOL_CALL_END,
                      toolCallId,
                    });
                  }
                  break;
                }

                case "action.result": {
                  const result = event.data.result;
                  if (result.kind !== "tool-result") break;

                  const toolCallId =
                    toolCallIdMap.get(result.callId) ?? `tc_${toolCallCounter++}`;

                  emit({
                    type: EventType.TOOL_CALL_RESULT,
                    toolCallId,
                    messageId: randomUUID(),
                    role: "tool",
                    content:
                      typeof result.output === "string"
                        ? result.output
                        : JSON.stringify(result.output ?? ""),
                  });
                  break;
                }

                case "message.appended": {
                  if (!textStarted) {
                    emit({
                      type: EventType.TEXT_MESSAGE_START,
                      messageId,
                      role: "assistant",
                    });
                    textStarted = true;
                  }
                  emit({
                    type: EventType.TEXT_MESSAGE_CONTENT,
                    messageId,
                    delta: event.data.messageDelta,
                  });
                  break;
                }

                case "message.completed": {
                  if (!textStarted) {
                    emit({
                      type: EventType.TEXT_MESSAGE_START,
                      messageId,
                      role: "assistant",
                    });
                    emit({
                      type: EventType.TEXT_MESSAGE_CONTENT,
                      messageId,
                      delta: event.data.message ?? "",
                    });
                    textStarted = true;
                  }
                  emit({
                    type: EventType.TEXT_MESSAGE_END,
                    messageId,
                  });
                  break;
                }

                case "turn.completed":
                case "session.waiting": {
                  if (!runFinished) {
                    runFinished = true;
                    emit({
                      type: EventType.RUN_FINISHED,
                      threadId,
                      runId,
                    });
                  }
                  // AG-UI is stateless per request — close the stream
                  reader.releaseLock();
                  controller.close();
                  return;
                }

                case "turn.failed":
                case "session.failed": {
                  emit({
                    type: EventType.RUN_ERROR,
                    message:
                      (event.data as { message?: string })?.message ??
                      "Agent run failed",
                  });
                  break;
                }

                default:
                  break;
              }
            }
          } catch (err) {
            emit({
              type: EventType.RUN_ERROR,
              message:
                err instanceof Error ? err.message : "Unknown stream error",
            });
          } finally {
            reader.releaseLock();
            controller.close();
          }
        },
      });

      return new Response(aguiStream, {
        headers: {
          "Content-Type": encoder.getContentType(),
          "Cache-Control": "no-cache",
          Connection: "keep-alive",
          "X-Thread-ID": threadId,
          "X-Run-ID": runId,
        },
      });
    }),
  ],
});
