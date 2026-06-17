import { REGISTRY } from "./registry.js";
import type { AgentArgs, AgentContext, AgentResult, Mode, PlanStep } from "./types.js";

/** Streaming execution events, mapped by callers onto AG-UI tool-call events. */
export type ExecEvent =
  | { kind: "agent_start"; tool: string; args: AgentArgs }
  | { kind: "agent_result"; tool: string; result: AgentResult };

function runAgent(step: PlanStep, context: AgentContext): Promise<AgentResult> {
  const def = REGISTRY[step.tool];
  if (!def) {
    return Promise.resolve({ agent: step.tool, ok: false, data: { error: `Unknown agent: ${step.tool}` } });
  }
  return def.execute(step.args, context);
}

/**
 * Execute the routed steps, yielding an event before and after each agent so
 * the transport layer can surface progress. Returns the accumulated results.
 * No LLM is involved here.
 *
 * - single / sequential: ordered; each step sees prior results as context.
 * - parallel: independent agents run concurrently (announced up front).
 */
export async function* executeStream(mode: Mode, steps: PlanStep[]): AsyncGenerator<ExecEvent, AgentContext> {
  const results: AgentContext = {};

  if (mode === "parallel") {
    for (const step of steps) {
      yield { kind: "agent_start", tool: step.tool, args: step.args };
    }
    const settled = await Promise.all(
      steps.map(async (step) => [step.tool, await runAgent(step, {})] as const),
    );
    for (const [tool, result] of settled) {
      results[tool] = result;
      yield { kind: "agent_result", tool, result };
    }
    return results;
  }

  for (const step of steps) {
    yield { kind: "agent_start", tool: step.tool, args: step.args };
    const result = await runAgent(step, results);
    results[step.tool] = result;
    yield { kind: "agent_result", tool: step.tool, result };
  }
  return results;
}
