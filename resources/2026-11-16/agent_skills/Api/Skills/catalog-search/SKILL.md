---
name: catalog-search
description: Search the product catalog by keyword, category, or maximum price. Use when the user wants to find, browse, or compare products.
---

# Catalog search

Use this skill when the user asks what products are available, wants to find a product, or wants options under a budget.

## Endpoint

`GET /catalog?q={query}&maxPrice={maxPrice}` on the same host that serves this MCP endpoint.

- `q` (optional): free text, for example `laptop`, `running shoes`, or `show me apple phones`.
- `maxPrice` (optional): only return products priced at or below this amount.
- With no `q`, every product is returned (still filtered by `maxPrice` if given).

The response is a JSON array of products. See `references/search-behavior.md` for how the query is matched and
for the response shape.

## Workflow

1. Turn the user's request into a short keyword query. Filler words are ignored, so the user's phrasing can be
   passed through as is.
2. If the user names a budget, pass it as `maxPrice`.
3. If the result is empty, retry with a broader keyword (for example the category instead of a brand), then tell
   the user nothing matched.
4. Present each product with its name and list price. Keep the `id`: it is required to get pricing and deals
   (see the `pricing` skill).
