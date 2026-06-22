import { z } from "zod";

/**
 * Environment configuration, validated once at import via loadConfig().
 *
 * Mirrors the config pattern from the original agent-orchestrator:
 * validates OPENAI_API_KEY, optional OPENAI_BASE_URL, and model names
 * so the process fails fast on bad config before anything else runs.
 */
const EnvSchema = z.object({
  OPENAI_API_KEY: z.string().min(1, "OPENAI_API_KEY is required (copy .env.example to .env)."),

  // Optional OpenAI-compatible endpoint (Azure, Ollama/vLLM, a proxy, ...).
  // An empty string is treated as "unset" so a blank .env line is fine.
  OPENAI_BASE_URL: z.preprocess(
    (v) => (v === "" ? undefined : v),
    z.string().url("OPENAI_BASE_URL must be a valid URL.").optional(),
  ),

  // The model used by the Eve agent for tool routing and synthesis.
  OPENAI_MODEL: z.string().min(1).default("gpt-4o"),
});

export type Config = z.infer<typeof EnvSchema>;

let current: Config | undefined;

/** Validate process.env and cache the result. Throws on failure. */
export function loadConfig(): Config {
  if (current) return current;

  const result = EnvSchema.safeParse(process.env);
  if (!result.success) {
    const issues = result.error.issues
      .map((i) => `  - ${i.path.join(".") || "(root)"}: ${i.message}`)
      .join("\n");
    throw new Error(`Invalid environment configuration:\n${issues}`);
  }
  current = result.data;
  return current;
}

/** Get the validated config. Throws if loadConfig() has not run yet. */
export function getConfig(): Config {
  if (!current) {
    throw new Error("Config not loaded — call loadConfig() at startup before using getConfig().");
  }
  return current;
}
