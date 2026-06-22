import { createOpenAI } from "@ai-sdk/openai";
import { defineAgent } from "eve";
import { loadConfig } from "#lib/config.js";

const config = loadConfig();

const openai = createOpenAI({
  apiKey: config.OPENAI_API_KEY,
  baseURL: config.OPENAI_BASE_URL,
});

export default defineAgent({
  model: openai(config.OPENAI_MODEL),
  // Required when using a direct LanguageModel (not a gateway model id string)
  // so eve can compute compaction thresholds.
  modelContextWindowTokens: 128_000,
});

