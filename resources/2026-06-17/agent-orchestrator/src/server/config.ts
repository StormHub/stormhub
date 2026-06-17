import { z } from "zod";

/**
 * Environment configuration, validated once at startup via loadConfig().
 *
 * loadConfig() is called explicitly from src/index.ts (after dotenv loads .env)
 * so the process fails fast on bad config before anything else is imported.
 * Everything downstream reads the validated values through getConfig().
 */
const EnvSchema = z.object({
  OPENAI_API_KEY: z.string().min(1, "OPENAI_API_KEY is required (copy .env.example to .env)."),

  // Optional OpenAI-compatible endpoint (Azure, Ollama/vLLM, a proxy, ...).
  // An empty string is treated as "unset" so a blank .env line is fine.
  OPENAI_BASE_URL: z.preprocess(
    (v) => (v === "" ? undefined : v),
    z.string().url("OPENAI_BASE_URL must be a valid URL.").optional(),
  ),

  PORT: z.coerce.number().int().positive(),
  ROUTER_MODEL: z.string().min(1),
  SYNTH_MODEL: z.string().min(1),
});

export type Config = z.infer<typeof EnvSchema>;

let current: Config | undefined;

/** Validate process.env and cache the result. Exits the process on failure. */
export function loadConfig(): Config {
  const result = EnvSchema.safeParse(process.env);
  if (!result.success) {
    const issues = result.error.issues
      .map((i) => `  - ${i.path.join(".") || "(root)"}: ${i.message}`)
      .join("\n");
    console.error(`Invalid environment configuration:\n${issues}`);
    process.exit(1);
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
