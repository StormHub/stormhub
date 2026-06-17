import { resolveProduct, type Product } from "../data/catalog.js";
import type { AgentArgs, AgentContext } from "../orchestrator/types.js";

/**
 * Resolve the product an agent should act on.
 *
 * Priority:
 *   1. An explicit `product` / `productId` / `query` arg from the router.
 *   2. A product surfaced by an earlier step in a sequential plan (e.g. the
 *      catalog search). This is how data flows forward between sequential steps.
 */
export function resolveTargetProduct(args: AgentArgs, context: AgentContext): Product | undefined {
  const ref =
    (args.productId as string | undefined) ??
    (args.product as string | undefined) ??
    (args.query as string | undefined);

  const direct = resolveProduct(ref);
  if (direct) return direct;

  // Look back through prior results for a product the catalog agent found.
  const catalogResult = context["catalog_agent__search_products"];
  if (catalogResult?.ok) {
    const data = catalogResult.data as { products?: Array<{ id: string }> };
    const first = data.products?.[0];
    if (first) return resolveProduct(first.id);
  }
  return undefined;
}
