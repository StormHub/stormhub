import { defineTool } from "eve/tools";
import { z } from "zod";
import { listCategories } from "#lib/catalog-data.js";

export default defineTool({
  description:
    "List the product categories the store carries (with a count per category). Use for broad questions like 'what do you have', 'what do you sell', or 'what categories are there'.",
  inputSchema: z.object({}),
  async execute() {
    const categories = listCategories();
    return { count: categories.length, categories };
  },
});
