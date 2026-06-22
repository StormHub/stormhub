import { defineTool } from "eve/tools";
import { z } from "zod";
import { resolveProduct, STOCK, DEALS } from "#lib/catalog-data.js";

export default defineTool({
  description:
    "Place an order for a product. Use after confirming the product exists and is in stock.",
  inputSchema: z.object({
    product: z.string().describe("Product name, id, or description to order."),
  }),
  async execute({ product }) {
    const resolved = resolveProduct(product);
    if (!resolved) {
      return { error: "No product specified or found to order." };
    }

    const units = STOCK[resolved.id] ?? 0;
    if (units <= 0) {
      return {
        productId: resolved.id,
        name: resolved.name,
        error: "Out of stock — order not placed.",
      };
    }

    const deal = DEALS[resolved.id] ?? { discountPct: 0 };
    const pricePaid = Math.round(resolved.price * (1 - deal.discountPct / 100) * 100) / 100;
    const confirmationId = `ORD-${Date.now().toString(36).toUpperCase()}-${resolved.id.slice(-4).toUpperCase()}`;

    return {
      confirmationId,
      productId: resolved.id,
      name: resolved.name,
      quantity: 1,
      pricePaid,
      status: "confirmed",
    };
  },
});
