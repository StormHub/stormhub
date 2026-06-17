import { DEALS, STOCK } from "../data/catalog.js";
import { resolveTargetProduct } from "../lib/resolve-product.js";
import type { AgentDefinition, AgentResult, AgentArgs, AgentContext } from "../orchestrator/types.js";

const AGENT = "Order Agent";

/**
 * Place a (fake) order. In a sequential plan this runs last and resolves its
 * product from context populated by the earlier catalog/inventory steps.
 */
async function placeOrder(args: AgentArgs, context: AgentContext): Promise<AgentResult> {
  const product = resolveTargetProduct(args, context);
  if (!product) {
    return { agent: AGENT, ok: false, data: { error: "No product specified or found to order." } };
  }

  const units = STOCK[product.id] ?? 0;
  if (units <= 0) {
    return {
      agent: AGENT,
      ok: false,
      data: { productId: product.id, name: product.name, error: "Out of stock — order not placed." },
    };
  }

  const deal = DEALS[product.id] ?? { discountPct: 0 };
  const pricePaid = Math.round(product.price * (1 - deal.discountPct / 100) * 100) / 100;
  const confirmationId = `ORD-${Date.now().toString(36).toUpperCase()}-${product.id.slice(-4).toUpperCase()}`;

  return {
    agent: AGENT,
    ok: true,
    data: {
      confirmationId,
      productId: product.id,
      name: product.name,
      quantity: 1,
      pricePaid,
      status: "confirmed",
    },
  };
}

export const orderAgent: AgentDefinition = {
  agent: AGENT,
  description: "Place an order for a product. Use after confirming the product exists and is in stock.",
  parameters: {
    type: "object",
    properties: {
      product: { type: "string", description: "Product name, id, or description to order." },
    },
    required: ["product"],
  },
  execute: placeOrder,
};
