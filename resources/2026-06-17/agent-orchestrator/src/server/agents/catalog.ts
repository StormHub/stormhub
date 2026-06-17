import { listCategories, searchCatalog } from "../data/catalog.js";
import type { AgentDefinition, AgentResult, AgentArgs } from "../orchestrator/types.js";

const AGENT = "Catalog Agent";

async function searchProducts(args: AgentArgs): Promise<AgentResult> {
  const query = (args.query as string | undefined) ?? "";
  const maxPrice = args.maxPrice as number | undefined;
  const products = searchCatalog(query, maxPrice).map((p) => ({
    id: p.id,
    name: p.name,
    price: p.price,
    category: p.category,
  }));
  return { agent: AGENT, ok: true, data: { query, maxPrice, count: products.length, products } };
}

async function getCategories(): Promise<AgentResult> {
  const categories = listCategories();
  return { agent: AGENT, ok: true, data: { count: categories.length, categories } };
}

export const catalogAgent: AgentDefinition = {
  agent: AGENT,
  description: "Search the product catalog by keyword or category (e.g. 'laptop', 'running shoes') with an optional maximum price. Returns matching products.",
  parameters: {
    type: "object",
    properties: {
      query: { type: "string", description: "Keyword or category to search for, e.g. 'laptop' or 'running shoes'." },
      maxPrice: { type: "number", description: "Optional maximum price filter in USD." },
    },
    required: ["query"],
  },
  execute: searchProducts,
};

export const catalogCategoriesAgent: AgentDefinition = {
  agent: AGENT,
  description:
    "List the product categories the store carries (with a count per category). Use for broad questions like 'what do you have', 'what do you sell', or 'what categories are there' — so the customer can pick a category to explore.",
  parameters: {
    type: "object",
    properties: {},
  },
  execute: getCategories,
};
