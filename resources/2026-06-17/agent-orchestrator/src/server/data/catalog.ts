/**
 * Mock in-memory shopping data. Deterministic, no external services.
 * Everything the agents return is derived from these tables.
 */

export interface Product {
  id: string;
  name: string;
  category: string;
  price: number;
  tags: string[];
}

export interface Review {
  rating: number; // average out of 5
  count: number;
  highlights: string[];
}

export interface Deal {
  /** Percentage off the list price, 0 if none. */
  discountPct: number;
  promo?: string;
}

export const CATALOG: Product[] = [
  { id: "p_laptop_dell", name: "Dell XPS 13", category: "laptop", price: 999, tags: ["laptop", "ultrabook", "work"] },
  { id: "p_laptop_mac", name: "MacBook Air M3", category: "laptop", price: 1199, tags: ["laptop", "apple", "work"] },
  { id: "p_phone_iphone15", name: "iPhone 15", category: "phone", price: 799, tags: ["phone", "apple", "ios"] },
  { id: "p_phone_pixel", name: "Pixel 8", category: "phone", price: 699, tags: ["phone", "google", "android"] },
  { id: "p_shoes_run", name: "Nike Pegasus 41 Running Shoes", category: "shoes", price: 130, tags: ["shoes", "running", "nike"] },
  { id: "p_shoes_trail", name: "Salomon Trail Runners", category: "shoes", price: 150, tags: ["shoes", "running", "trail"] },
  { id: "p_headphones", name: "Sony WH-1000XM5", category: "audio", price: 349, tags: ["headphones", "audio", "noise-cancelling"] },
];

/** product id -> units in stock */
export const STOCK: Record<string, number> = {
  p_laptop_dell: 12,
  p_laptop_mac: 0,
  p_phone_iphone15: 34,
  p_phone_pixel: 5,
  p_shoes_run: 80,
  p_shoes_trail: 0,
  p_headphones: 21,
};

/** product id -> active deal */
export const DEALS: Record<string, Deal> = {
  p_laptop_dell: { discountPct: 10, promo: "Summer Sale" },
  p_laptop_mac: { discountPct: 0 },
  p_phone_iphone15: { discountPct: 5, promo: "Trade-in bonus" },
  p_phone_pixel: { discountPct: 0 },
  p_shoes_run: { discountPct: 20, promo: "Clearance" },
  p_shoes_trail: { discountPct: 0 },
  p_headphones: { discountPct: 15, promo: "Summer Sale" },
};

/** product id -> reviews */
export const REVIEWS: Record<string, Review> = {
  p_laptop_dell: { rating: 4.5, count: 1280, highlights: ["Great keyboard", "Excellent battery life"] },
  p_laptop_mac: { rating: 4.7, count: 2100, highlights: ["Silent and fast", "Premium build"] },
  p_phone_iphone15: { rating: 4.6, count: 5400, highlights: ["Smooth performance", "Good cameras"] },
  p_phone_pixel: { rating: 4.4, count: 1900, highlights: ["Best-in-class camera", "Clean Android"] },
  p_shoes_run: { rating: 4.6, count: 3200, highlights: ["Comfortable for long runs", "Durable"] },
  p_shoes_trail: { rating: 4.3, count: 540, highlights: ["Great grip", "Runs small"] },
  p_headphones: { rating: 4.8, count: 4100, highlights: ["Top-tier noise cancelling", "Comfortable"] },
};

/** Filler words stripped from a search query before matching. */
const STOPWORDS = new Set([
  "show", "me", "find", "get", "search", "for", "the", "a", "an", "any", "some",
  "in", "of", "stock", "please", "see", "want", "need", "looking", "like", "with",
  "do", "you", "have", "got", "available", "sell", "selling", "your", "is", "are",
]);

/** Tokenize a query: split on non-alphanumerics, drop stopwords, crudely singularize. */
function searchTerms(query: string): string[] {
  return query
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter((t) => t && !STOPWORDS.has(t))
    .map((t) => (t.length > 3 && t.endsWith("s") ? t.slice(0, -1) : t));
}

/**
 * Find products by free-text query (keyword or category) and optional max price.
 * Keyword match over name + category + tags; good enough for a deterministic demo.
 * For a broad "what do you have" overview, use listCategories() instead.
 */
export function searchCatalog(query: string, maxPrice?: number): Product[] {
  const terms = searchTerms(query);
  return CATALOG.filter((p) => {
    if (maxPrice !== undefined && p.price > maxPrice) return false;
    if (terms.length === 0) return true;
    // Match on whole words (equality or prefix) so e.g. "phone" doesn't match
    // "headphones". Prefix keeps plurals/stems working ("shoe" -> "shoes").
    const tokens = `${p.name} ${p.category} ${p.tags.join(" ")}`
      .toLowerCase()
      .split(/[^a-z0-9]+/)
      .filter(Boolean);
    return terms.some((t) => tokens.some((tok) => tok === t || tok.startsWith(t)));
  });
}

/**
 * The distinct product categories with how many products are in each, derived
 * from the catalog. Used for broad "what do you have / what do you sell" queries
 * so the assistant can offer categories to drill into rather than dumping every
 * product.
 */
export function listCategories(): { category: string; count: number }[] {
  const counts = new Map<string, number>();
  for (const p of CATALOG) {
    counts.set(p.category, (counts.get(p.category) ?? 0) + 1);
  }
  return [...counts].map(([category, count]) => ({ category, count }));
}

/**
 * Resolve a product from either an explicit id, a name, or a free-text query.
 * Used by agents that operate on a single product (stock, pricing, reviews, order).
 */
export function resolveProduct(ref: string | undefined): Product | undefined {
  if (!ref) return undefined;
  const lowered = ref.toLowerCase();
  const byId = CATALOG.find((p) => p.id === ref);
  if (byId) return byId;
  const byName = CATALOG.find((p) => p.name.toLowerCase() === lowered);
  if (byName) return byName;
  // fall back to best keyword match
  return searchCatalog(ref)[0];
}
