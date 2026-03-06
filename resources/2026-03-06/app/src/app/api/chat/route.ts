import type { UIMessage } from "ai";
import { createUIMessageStream, createUIMessageStreamResponse } from "ai";

const AGUI_BACKEND_URL =
  process.env.AGUI_BACKEND_URL || "http://localhost:5000";

export const maxDuration = 60;

interface AGUIMessage {
  id: string;
  role: string;
  content: string;
}

interface AGUIRunInput {
  threadId: string;
  runId: string;
  messages: AGUIMessage[];
  tools: unknown[];
  context: unknown[];
}

function convertToAGUIMessages(messages: UIMessage[]): AGUIMessage[] {
  return messages.map((msg) => ({
    id: msg.id,
    role: msg.role,
    content:
      msg.parts
        ?.filter((p) => p.type === "text")
        .map((p) => (p as { type: "text"; text: string }).text)
        .join("") || "",
  }));
}

export async function POST(req: Request) {
  const { messages }: { messages: UIMessage[] } = await req.json();

  const aguiPayload: AGUIRunInput = {
    threadId: crypto.randomUUID(),
    runId: crypto.randomUUID(),
    messages: convertToAGUIMessages(messages),
    tools: [],
    context: [],
  };

  const backendResponse = await fetch(AGUI_BACKEND_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(aguiPayload),
  });

  if (!backendResponse.ok || !backendResponse.body) {
    return new Response(
      JSON.stringify({ error: "Backend request failed" }),
      { status: 502, headers: { "Content-Type": "application/json" } }
    );
  }

  const stream = createUIMessageStream({
    execute: async ({ writer }) => {
      const reader = backendResponse.body!.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      let textId = "";
      let textStarted = false;
      let inStep = false;
      const toolCallArgs: Record<string, { name: string; args: string }> = {};

      function ensureStep() {
        if (!inStep) {
          writer.write({ type: "start-step" });
          inStep = true;
        }
      }

      function closeStep() {
        if (inStep) {
          writer.write({ type: "finish-step" });
          inStep = false;
        }
      }

      function ensureTextStarted() {
        if (!textStarted) {
          ensureStep();
          textId = crypto.randomUUID();
          writer.write({ type: "text-start", id: textId });
          textStarted = true;
        }
      }

      function closeText() {
        if (textStarted) {
          writer.write({ type: "text-end", id: textId });
          textStarted = false;
          textId = "";
        }
      }

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split("\n");
        buffer = lines.pop() || "";

        for (const line of lines) {
          const trimmed = line.trim();
          if (!trimmed || !trimmed.startsWith("data:")) continue;

          const jsonStr = trimmed.slice(5).trim();
          if (!jsonStr || jsonStr === "[DONE]") continue;

          let event: Record<string, unknown>;
          try {
            event = JSON.parse(jsonStr);
          } catch {
            continue;
          }

          const eventType = event.type as string;

          switch (eventType) {
            case "TEXT_MESSAGE_START": {
              ensureTextStarted();
              break;
            }

            case "TEXT_MESSAGE_CONTENT": {
              const delta = event.delta as string;
              if (delta) {
                ensureTextStarted();
                writer.write({ type: "text-delta", id: textId, delta });
              }
              break;
            }

            case "TEXT_MESSAGE_END": {
              closeText();
              break;
            }

            case "TEXT_MESSAGE_CHUNK": {
              const chunkDelta = event.delta as string;
              if (chunkDelta) {
                ensureTextStarted();
                writer.write({
                  type: "text-delta",
                  id: textId,
                  delta: chunkDelta,
                });
              }
              break;
            }

            case "TOOL_CALL_START": {
              closeText();
              closeStep();
              ensureStep();
              const toolCallId = event.toolCallId as string;
              const toolName = event.toolCallName as string;
              toolCallArgs[toolCallId] = { name: toolName, args: "" };
              writer.write({ type: "tool-input-start", toolCallId, toolName });
              break;
            }

            case "TOOL_CALL_ARGS": {
              const toolCallId = event.toolCallId as string;
              const argsDelta = event.delta as string;
              if (toolCallArgs[toolCallId] && argsDelta) {
                toolCallArgs[toolCallId].args += argsDelta;
                writer.write({
                  type: "tool-input-delta",
                  toolCallId,
                  inputTextDelta: argsDelta,
                });
              }
              break;
            }

            case "TOOL_CALL_CHUNK": {
              const toolCallId = event.toolCallId as string;
              const toolName = event.toolCallName as string;
              const argsDelta = event.delta as string;
              if (!toolCallArgs[toolCallId]) {
                closeText();
                closeStep();
                ensureStep();
                toolCallArgs[toolCallId] = { name: toolName || "", args: "" };
                if (toolName) {
                  writer.write({
                    type: "tool-input-start",
                    toolCallId,
                    toolName,
                  });
                }
              }
              if (argsDelta) {
                toolCallArgs[toolCallId].args += argsDelta;
                writer.write({
                  type: "tool-input-delta",
                  toolCallId,
                  inputTextDelta: argsDelta,
                });
              }
              break;
            }

            case "TOOL_CALL_END": {
              const toolCallId = event.toolCallId as string;
              const tc = toolCallArgs[toolCallId];
              if (tc) {
                let parsedInput = {};
                try {
                  parsedInput = JSON.parse(tc.args);
                } catch {
                  parsedInput = { raw: tc.args };
                }
                writer.write({
                  type: "tool-input-available",
                  toolCallId,
                  toolName: tc.name,
                  input: parsedInput,
                });
              }
              break;
            }

            case "TOOL_CALL_RESULT": {
              const toolCallId = event.toolCallId as string;
              const content = event.content as string;
              const tc = toolCallArgs[toolCallId];
              writer.write({
                type: "tool-output-available",
                toolCallId,
                output: content,
              });
              break;
            }

            case "RUN_ERROR": {
              const errorMessage =
                (event.message as string) || "Unknown error";
              writer.write({ type: "error", errorText: errorMessage });
              break;
            }

            case "RUN_STARTED":
            case "RUN_FINISHED":
            case "STEP_STARTED":
            case "STEP_FINISHED":
              break;

            default:
              break;
          }
        }
      }

      // Close any open text and step
      closeText();
      closeStep();
    },
  });

  return createUIMessageStreamResponse({ stream });
}
