import type OpenAI from "openai";
import type { AgentDefinition } from "./types.js";

import { catalogAgent, catalogCategoriesAgent } from "../agents/catalog.js";
import { inventoryAgent } from "../agents/inventory.js";
import { pricingAgent } from "../agents/pricing.js";
import { reviewsAgent } from "../agents/reviews.js";
import { orderAgent } from "../agents/order.js";

/**
 * The agent registry: a plain in-process map of skill name -> definition.
 *
 * Agents are registered MANUALLY here. There is intentionally no Redis, no DB,
 * and no HTTP self-registration — keeping everything in plain TypeScript makes
 * the orchestrator trivially testable and runnable with zero infrastructure.
 */
export const REGISTRY: Record<string, AgentDefinition> = {
  catalog_agent__list_categories: catalogCategoriesAgent,
  catalog_agent__search_products: catalogAgent,
  inventory_agent__check_stock: inventoryAgent,
  pricing_agent__get_deals: pricingAgent,
  reviews_agent__get_reviews: reviewsAgent,
  order_agent__place_order: orderAgent,
};

/**
 * The `plan_execution` meta-tool. It is a SIGNAL, not an agent: when the router
 * picks it, the orchestrator switches to sequential mode. Unlike the original
 * article (which leaves ordering/context-passing unspecified), we have the
 * router also return the ordered `steps`, so sequential plans actually execute
 * with data flowing forward between steps.
 */
export const PLAN_EXECUTION_TOOL = "plan_execution";

/**
 * Build the OpenAI tool definitions from the registry plus the meta-tool.
 * This is what the router LLM sees and chooses from.
 */
export function toolDefinitions(): OpenAI.Chat.Completions.ChatCompletionTool[] {
  const agentTools = Object.entries(REGISTRY).map(([name, def]) => ({
    type: "function" as const,
    function: {
      name,
      description: `[${def.agent}] ${def.description}`,
      parameters: def.parameters,
    },
  }));

  const planTool: OpenAI.Chat.Completions.ChatCompletionTool = {
    type: "function",
    function: {
      name: PLAN_EXECUTION_TOOL,
      description:
        "Use this ONLY when the query requires sequential steps where a later step DEPENDS on the result of an earlier step (e.g. find a product, then order it). Provide the ordered steps.",
      parameters: {
        type: "object",
        properties: {
          reason: { type: "string", description: "Why sequential execution is required." },
          steps: {
            type: "array",
            description: "Ordered list of agent tools to run, earliest first.",
            items: {
              type: "object",
              properties: {
                tool: {
                  type: "string",
                  enum: Object.keys(REGISTRY),
                  description: "The agent tool name to run for this step.",
                },
                args: {
                  type: "object",
                  description: "Arguments for this step's tool. May be empty if it depends on prior results.",
                  additionalProperties: true,
                },
                reason: { type: "string", description: "Why this step is needed." },
              },
              required: ["tool"],
            },
          },
        },
        required: ["reason", "steps"],
      },
    },
  };

  return [...agentTools, planTool];
}
