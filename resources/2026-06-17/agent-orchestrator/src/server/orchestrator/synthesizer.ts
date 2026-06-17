import { getConfig } from "../config.js";
import { getOpenAIClient } from "../llm/openai.js";
import type { AgentContext } from "./types.js";

const SYSTEM_PROMPT = "Summarize the agent results into a clear, helpful answer.";

/**
 * LLM call 2, streamed. Turns the structured agent results into a
 * natural-language answer (temperature 0.7), yielding content deltas as they
 * arrive so the transport can stream them as TEXT_MESSAGE_CONTENT events.
 */
export async function* synthesizeStream(query: string, results: AgentContext): AsyncGenerator<string> {
  const stream = await getOpenAIClient().chat.completions.create({
    model: getConfig().SYNTH_MODEL,
    temperature: 0.7,
    stream: true,
    messages: [
      { role: "system", content: SYSTEM_PROMPT },
      { role: "user", content: `User asked: ${query}\nResults: ${JSON.stringify(results)}` },
    ],
  });

  for await (const chunk of stream) {
    const delta = chunk.choices[0]?.delta?.content;
    if (delta) yield delta;
  }
}
