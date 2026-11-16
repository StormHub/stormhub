---
name: pricing
description: Get the list price, active promotion, and final discounted price for a product by its id. Use when the user asks how much a product costs or whether it is on sale.
---

# Pricing

Use this skill when the user asks about the price, discounts, or promotions for a specific product.

## Endpoint

`GET /pricing/{productId}` on the same host that serves this MCP endpoint.

`productId` must be an exact product id such as `p_laptop_dell`. Names and free text are not accepted. If you only
have a name or description, find the id first with the `catalog-search` skill.

## Response

```json
{
  "productId": "p_laptop_dell",
  "name": "Dell XPS 13",
  "listPrice": 999,
  "discountPct": 10,
  "promo": "Summer Sale",
  "finalPrice": 899.1
}
```

- `discountPct` is `0` and `promo` is `null` when there is no active deal.
- An unknown id returns HTTP 404 with `{ "error": "Product '<id>' not found." }`.

## Workflow

1. Resolve the product id (from earlier search results, or by searching the catalog).
2. Call the endpoint.
3. Quote `finalPrice` to the user. When `discountPct` is above 0, also mention the list price, the discount, and
   the promo name.
