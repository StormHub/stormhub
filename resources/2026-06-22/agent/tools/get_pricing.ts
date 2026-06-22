import { defineTool } from "eve/tools";
import { z } from "zod";
import { resolveProduct, DEALS } from "#lib/catalog-data.js";

export default defineTool({
  description: "Get the current price, active promotions, and final discounted price for a product.",
  inputSchema: z.object({
    product: z.string().describe("Product name, id, or description to price."),
  }),
  async execute({ product }) {
    const resolved = resolveProduct(product);
    if (!resolved) {
      return { error: "No product specified or found." };
    }
    const deal = DEALS[resolved.id] ?? { discountPct: 0 };
    const finalPrice = Math.round(resolved.price * (1 - deal.discountPct / 100) * 100) / 100;
    return {
      productId: resolved.id,
      name: resolved.name,
      listPrice: resolved.price,
      discountPct: deal.discountPct,
      promo: deal.promo ?? null,
      finalPrice,
    };
  },
});
