"use client";

import { useState } from "react";
import { useChat } from "@ai-sdk/react";
import { DefaultChatTransport } from "ai";
import { CloudSun } from "lucide-react";
import {
  Conversation,
  ConversationContent,
  ConversationScrollButton,
  ConversationEmptyState,
} from "@/components/ai-elements/conversation";
import {
  Message,
  MessageContent,
  MessageResponse,
} from "@/components/ai-elements/message";
import {
  PromptInput,
  PromptInputTextarea,
  PromptInputSubmit,
  type PromptInputMessage,
} from "@/components/ai-elements/prompt-input";

export default function ChatPage() {
  const [input, setInput] = useState("");

  const { messages, sendMessage, status, stop } = useChat({
    transport: new DefaultChatTransport({
      api: "/api/chat",
    }),
  });

  const handleSubmit = (message: PromptInputMessage) => {
    if (!message.text?.trim()) return;
    sendMessage({ text: message.text });
    setInput("");
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-4">
      <div className="flex flex-col w-full max-w-3xl h-[85vh] rounded-xl border bg-card shadow-sm">
        <div className="flex items-center gap-2 border-b px-6 py-4">
          <CloudSun className="size-5 text-primary" />
          <h1 className="text-lg font-semibold">Weather Agent</h1>
        </div>

        <div className="flex flex-col flex-1 overflow-hidden p-4">
          <Conversation>
            <ConversationContent>
              {messages.length === 0 ? (
                <ConversationEmptyState
                  icon={<CloudSun className="size-12" />}
                  title="Weather Forecast Agent"
                  description='Ask me about the weather in any city! Try: "What is the weather like in Tokyo tomorrow?"'
                />
              ) : (
                <>
                  {messages.map((message) => (
                    <Message from={message.role} key={message.id}>
                      <MessageContent>
                        {message.parts.map((part, i) => {
                          switch (part.type) {
                            case "text":
                              return (
                                <MessageResponse key={`${message.id}-${i}`}>
                                  {part.text}
                                </MessageResponse>
                              );
                            default: {
                              if (
                                "toolCallId" in part &&
                                "toolName" in part
                              ) {
                                const toolPart = part as {
                                  toolCallId: string;
                                  toolName: string;
                                  state: string;
                                };
                                return (
                                  <div
                                    key={`${message.id}-${i}`}
                                    className="my-2 rounded-lg border bg-muted/50 px-4 py-3 text-sm"
                                  >
                                    <div className="flex items-center gap-2">
                                      {toolPart.state === "output-available" ? (
                                        <span className="text-green-600">✓</span>
                                      ) : (
                                        <span className="inline-block size-3 animate-spin rounded-full border-2 border-current border-t-transparent" />
                                      )}
                                      <span className="font-medium text-muted-foreground">
                                        {toolPart.toolName}
                                      </span>
                                      {toolPart.state !== "output-available" && (
                                        <span className="text-xs text-muted-foreground">
                                          Running…
                                        </span>
                                      )}
                                    </div>
                                  </div>
                                );
                              }
                              return null;
                            }
                          }
                        })}
                      </MessageContent>
                    </Message>
                  ))}

                  {/* Thinking indicator when waiting for first token */}
                  {status !== "ready" && status !== "error" && (
                    messages.length === 0 ||
                    messages[messages.length - 1].role === "user" ||
                    !messages[messages.length - 1].parts.some((p) => p.type === "text" && (p as { type: "text"; text: string }).text)
                  ) && (
                    <Message from="assistant">
                      <MessageContent>
                        <div className="flex items-center gap-2 text-muted-foreground py-1">
                          <span className="flex gap-1">
                            <span className="size-2 rounded-full bg-current animate-bounce [animation-delay:0ms]" />
                            <span className="size-2 rounded-full bg-current animate-bounce [animation-delay:150ms]" />
                            <span className="size-2 rounded-full bg-current animate-bounce [animation-delay:300ms]" />
                          </span>
                          <span className="text-sm">Thinking…</span>
                        </div>
                      </MessageContent>
                    </Message>
                  )}
                </>
              )}
            </ConversationContent>
            <ConversationScrollButton />
          </Conversation>

          <PromptInput
            onSubmit={handleSubmit}
            className="mt-4 w-full max-w-2xl mx-auto relative"
          >
            <PromptInputTextarea
              value={input}
              placeholder="Ask about the weather..."
              onChange={(e) => setInput(e.currentTarget.value)}
              className="pr-12"
            />
            <PromptInputSubmit
              status={status === "streaming" ? "streaming" : "ready"}
              disabled={!input.trim() && status === "ready"}
              onClick={
                status === "streaming" ? () => stop() : undefined
              }
              className="absolute bottom-1 right-1"
            />
          </PromptInput>
        </div>
      </div>
    </main>
  );
}
