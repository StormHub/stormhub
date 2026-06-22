import { defineTool } from "eve/tools";
import { z } from "zod";
import { resolveProduct, REVIEWS } from "#lib/catalog-data.js";

export default defineTool({
  description: "Get the aggregate rating, review count, and top review highlights for a product.",
  inputSchema: z.object({
    product: z.string().describe("Product name, id, or description to fetch reviews for."),
  }),
  async execute({ product }) {
    const resolved = resolveProduct(product);
    if (!resolved) {
      return { error: "No product specified or found." };
    }
    const review = REVIEWS[resolved.id];
    if (!review) {
      return { productId: resolved.id, name: resolved.name, rating: null, count: 0, highlights: [] };
    }
    return {
      productId: resolved.id,
      name: resolved.name,
      ...review,
    };
  },
});
