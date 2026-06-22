import { defineTool } from "eve/tools";
import { z } from "zod";
import { searchCatalog } from "#lib/catalog-data.js";

export default defineTool({
  description:
    "Search the product catalog by keyword or category (e.g. 'laptop', 'running shoes') with an optional maximum price. Returns matching products with id, name, price, and category.",
  inputSchema: z.object({
    query: z.string().describe("Keyword or category to search for, e.g. 'laptop' or 'running shoes'."),
    maxPrice: z.number().optional().describe("Optional maximum price filter in USD."),
  }),
  async execute({ query, maxPrice }) {
    const products = searchCatalog(query, maxPrice).map((p) => ({
      id: p.id,
      name: p.name,
      price: p.price,
      category: p.category,
    }));
    return { query, maxPrice, count: products.length, products };
  },
});
