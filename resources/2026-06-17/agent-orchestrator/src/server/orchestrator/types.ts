/**
 * Core types for the Orchestrator pattern.
 *
 * The orchestrator runs three phases with exactly two LLM calls:
 *   1. route()      - LLM call 1: pick tool(s), do not answer
 *   2. execute()    - no LLM: run the selected agents
 *   3. synthesize() - LLM call 2: turn results into prose
 */

/** Arbitrary JSON arguments the router fills in for an agent. */
export type AgentArgs = Record<string, unknown>;

/** Accumulated results from agents that have already run (used by sequential steps). */
export type AgentContext = Record<string, AgentResult>;

/** What an agent returns. `data` is whatever structured payload it produces. */
export interface AgentResult {
  agent: string;
  ok: boolean;
  data: unknown;
}

/**
 * An agent's execute function. Receives the router-provided args and the
 * context of prior results (only non-empty in sequential mode).
 */
export type ExecuteFn = (args: AgentArgs, context: AgentContext) => Promise<AgentResult>;

/** A registered agent: its metadata, JSON-schema params, and execute fn. */
export interface AgentDefinition {
  /** Human-readable agent name, e.g. "Catalog Agent". */
  agent: string;
  /** Description shown to the router LLM so it can choose this tool. */
  description: string;
  /** JSON Schema for the arguments this agent accepts. */
  parameters: Record<string, unknown>;
  execute: ExecuteFn;
}

export type Mode = "single" | "parallel" | "sequential";

/** One ordered step in a sequential plan. */
export interface PlanStep {
  tool: string;
  args: AgentArgs;
  reason?: string;
}

/** The router's decision after LLM call 1. */
export interface RouteDecision {
  mode: Mode;
  /** Tools to run. For sequential mode these are ordered. */
  steps: PlanStep[];
}

/** A single entry in the human-readable execution trace. */
export interface TraceEntry {
  phase: "route" | "execute" | "synthesize";
  detail: string;
}

/** The full result returned by orchestrate(). */
export interface OrchestrationResult {
  query: string;
  mode: Mode;
  agents: string[];
  trace: TraceEntry[];
  results: AgentContext;
  answer: string;
}
