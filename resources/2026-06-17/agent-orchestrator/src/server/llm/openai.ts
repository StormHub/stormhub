import OpenAI from "openai";
import { getConfig } from "../config.js";

/** Build an OpenAI client from the validated config. */
export function getOpenAIClient(): OpenAI {
  const { OPENAI_API_KEY, OPENAI_BASE_URL } = getConfig();
  return new OpenAI({ apiKey: OPENAI_API_KEY, baseURL: OPENAI_BASE_URL });
}
