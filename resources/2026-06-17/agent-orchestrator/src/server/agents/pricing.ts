import { DEALS } from "../data/catalog.js";
import { resolveTargetProduct } from "../lib/resolve-product.js";
import type { AgentDefinition, AgentResult, AgentArgs, AgentContext } from "../orchestrator/types.js";

const AGENT = "Pricing Agent";

async function getDeals(args: AgentArgs, context: AgentContext): Promise<AgentResult> {
  const product = resolveTargetProduct(args, context);
  if (!product) {
    return { agent: AGENT, ok: false, data: { error: "No product specified or found." } };
  }
  const deal = DEALS[product.id] ?? { discountPct: 0 };
  const finalPrice = Math.round(product.price * (1 - deal.discountPct / 100) * 100) / 100;
  return {
    agent: AGENT,
    ok: true,
    data: {
      productId: product.id,
      name: product.name,
      listPrice: product.price,
      discountPct: deal.discountPct,
      promo: deal.promo ?? null,
      finalPrice,
    },
  };
}

export const pricingAgent: AgentDefinition = {
  agent: AGENT,
  description: "Get the current price, active promotions, and final discounted price for a product.",
  parameters: {
    type: "object",
    properties: {
      product: { type: "string", description: "Product name, id, or description to price." },
    },
    required: ["product"],
  },
  execute: getDeals,
};
