import { REVIEWS } from "../data/catalog.js";
import { resolveTargetProduct } from "../lib/resolve-product.js";
import type { AgentDefinition, AgentResult, AgentArgs, AgentContext } from "../orchestrator/types.js";

const AGENT = "Reviews Agent";

async function getReviews(args: AgentArgs, context: AgentContext): Promise<AgentResult> {
  const product = resolveTargetProduct(args, context);
  if (!product) {
    return { agent: AGENT, ok: false, data: { error: "No product specified or found." } };
  }
  const review = REVIEWS[product.id];
  if (!review) {
    return { agent: AGENT, ok: true, data: { productId: product.id, name: product.name, rating: null, count: 0, highlights: [] } };
  }
  return {
    agent: AGENT,
    ok: true,
    data: { productId: product.id, name: product.name, ...review },
  };
}

export const reviewsAgent: AgentDefinition = {
  agent: AGENT,
  description: "Get the aggregate rating, review count, and top review highlights for a product.",
  parameters: {
    type: "object",
    properties: {
      product: { type: "string", description: "Product name, id, or description to fetch reviews for." },
    },
    required: ["product"],
  },
  execute: getReviews,
};
