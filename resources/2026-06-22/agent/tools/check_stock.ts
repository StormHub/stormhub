import { defineTool } from "eve/tools";
import { z } from "zod";
import { resolveProduct, STOCK } from "#lib/catalog-data.js";

export default defineTool({
  description: "Check stock level and availability for a specific product.",
  inputSchema: z.object({
    product: z.string().describe("Product name, id, or description to check stock for."),
  }),
  async execute({ product }) {
    const resolved = resolveProduct(product);
    if (!resolved) {
      return { error: "No product specified or found." };
    }
    const units = STOCK[resolved.id] ?? 0;
    return {
      productId: resolved.id,
      name: resolved.name,
      inStock: units > 0,
      units,
    };
  },
});
