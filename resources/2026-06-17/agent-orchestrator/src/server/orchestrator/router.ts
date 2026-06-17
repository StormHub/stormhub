import { getConfig } from "../config.js";
import { getOpenAIClient } from "../llm/openai.js";
import { PLAN_EXECUTION_TOOL, toolDefinitions } from "./registry.js";
import type { AgentArgs, PlanStep, RouteDecision } from "./types.js";

const SYSTEM_PROMPT = `You are a query router. Your ONLY job is to decide which tool(s) to call.
Rules:
- If the query needs ONE agent, call that one tool.
- If the query needs MULTIPLE INDEPENDENT agents, call all of them.
- If the query needs steps IN ORDER (a later step depends on an earlier one), call plan_execution and provide the ordered steps.
Do NOT answer the user's question — just pick tools.`;

function safeParseArgs(raw: string | undefined): AgentArgs {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw);
    return typeof parsed === "object" && parsed !== null ? (parsed as AgentArgs) : {};
  } catch {
    return {};
  }
}

/**
 * LLM call 1. Asks the model to pick tools (temperature 0, deterministic).
 * Returns the execution mode plus the ordered/parallel steps to run.
 */
export async function route(query: string): Promise<RouteDecision> {
  const response = await getOpenAIClient().chat.completions.create({
    model: getConfig().ROUTER_MODEL,
    temperature: 0,
    tools: toolDefinitions(),
    tool_choice: "auto",
    messages: [
      { role: "system", content: SYSTEM_PROMPT },
      { role: "user", content: query },
    ],
  });

  const toolCalls = response.choices[0]?.message.tool_calls ?? [];

  // 0 tool calls -> nothing to route; treat as a single no-op (synthesizer answers directly).
  if (toolCalls.length === 0) {
    return { mode: "single", steps: [] };
  }

  // plan_execution present -> sequential. Take its ordered steps.
  const planCall = toolCalls.find((c) => c.function.name === PLAN_EXECUTION_TOOL);
  if (planCall) {
    const parsed = safeParseArgs(planCall.function.arguments) as {
      steps?: Array<{ tool: string; args?: AgentArgs; reason?: string }>;
    };
    const steps: PlanStep[] = (parsed.steps ?? []).map((s) => ({
      tool: s.tool,
      args: s.args ?? {},
      reason: s.reason,
    }));
    return { mode: "sequential", steps };
  }

  // Map remaining tool calls to steps.
  const steps: PlanStep[] = toolCalls.map((c) => ({
    tool: c.function.name,
    args: safeParseArgs(c.function.arguments),
  }));

  return { mode: steps.length > 1 ? "parallel" : "single", steps };
}
