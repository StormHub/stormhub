import { randomUUID } from "node:crypto";
import * as readline from "node:readline/promises";
import { stdin, stdout } from "node:process";
import { HttpAgent, type AgentSubscriber } from "@ag-ui/client";

/**
 * Interactive AG-UI chat client. Start the server, then:
 *
 *   pnpm client
 *
 * Type a shopping request at the prompt and watch the streamed run: the tool
 * calls (execute phase) and the synthesized answer (streamed text). One
 * HttpAgent is reused for the whole session so the thread/history persists.
 * Type `exit` (or Ctrl-C) to quit.
 */
const url = process.env.SERVER_URL ?? "http://localhost:3000/";

const subscriber: AgentSubscriber = {
  onToolCallStartEvent: ({ event }) => {
    process.stdout.write(`\n⚙  ${event.toolCallName}`);
  },
  onToolCallArgsEvent: ({ event }) => {
    process.stdout.write(`  ${event.delta}`);
  },
  onToolCallResultEvent: ({ event }) => {
    process.stdout.write(`\n   → ${event.content}`);
  },
  onTextMessageStartEvent: () => {
    process.stdout.write(`\n\n💬 `);
  },
  onTextMessageContentEvent: ({ event }) => {
    process.stdout.write(event.delta);
  },
  onTextMessageEndEvent: () => {
    process.stdout.write(`\n`);
  },
  onRunErrorEvent: ({ event }) => {
    process.stderr.write(`\n✗  ${event.message}\n`);
  },
};

async function main() {
  const agent = new HttpAgent({ url, threadId: randomUUID() });
  const rl = readline.createInterface({ input: stdin, output: stdout, prompt: "you ▸ " });

  // stdin may close (Ctrl-D / piped EOF) mid-run; don't prompt after that.
  let closed = false;
  rl.on("close", () => {
    closed = true;
  });

  console.log(`Shopping agent chat — connected to ${url}`);
  console.log(`Type a request (e.g. "find a laptop under $1000 and order it"). Type "exit" or Ctrl-D to quit.\n`);
  rl.prompt();

  // Iterating the interface ends cleanly on EOF.
  for await (const line of rl) {
    const query = line.trim();
    if (query === "exit" || query === "quit") break;
    if (query !== "") {
      agent.addMessage({ id: randomUUID(), role: "user", content: query });
      await agent.runAgent({ runId: randomUUID() }, subscriber);
      process.stdout.write("\n");
    }
    if (closed) break;
    rl.prompt();
  }
  rl.close();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
