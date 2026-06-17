import { STOCK } from "../data/catalog.js";
import { resolveTargetProduct } from "../lib/resolve-product.js";
import type { AgentDefinition, AgentResult, AgentArgs, AgentContext } from "../orchestrator/types.js";

const AGENT = "Inventory Agent";

async function checkStock(args: AgentArgs, context: AgentContext): Promise<AgentResult> {
  const product = resolveTargetProduct(args, context);
  if (!product) {
    return { agent: AGENT, ok: false, data: { error: "No product specified or found." } };
  }
  const units = STOCK[product.id] ?? 0;
  return {
    agent: AGENT,
    ok: true,
    data: { productId: product.id, name: product.name, inStock: units > 0, units },
  };
}

export const inventoryAgent: AgentDefinition = {
  agent: AGENT,
  description: "Check stock level and availability for a specific product.",
  parameters: {
    type: "object",
    properties: {
      product: { type: "string", description: "Product name, id, or description to check stock for." },
    },
    required: ["product"],
  },
  execute: checkStock,
};
