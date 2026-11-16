namespace Api.Data;

public sealed record PriceQuote(
    string ProductId,
    string Name,
    decimal ListPrice,
    int DiscountPct,
    string? Promo,
    decimal FinalPrice);

/// <summary>
/// Current price, active promotions, and final discounted price for a product.
/// Ported from agent-orchestrator/src/server/agents/pricing.ts. Register with DI and inject where needed.
/// </summary>
public sealed class Pricing(Catalog catalog)
{
    /// <summary>Price a product by its id. Returns null if no product has that id.</summary>
    public PriceQuote? GetDeals(string productId)
    {
        var p = catalog.GetProduct(productId);
        if (p is null) return null;

        var deal = Catalog.Deals.GetValueOrDefault(p.Id) ?? new Deal(0);
        var finalPrice = Math.Round(p.Price * (1 - deal.DiscountPct / 100m), 2, MidpointRounding.AwayFromZero);
        return new PriceQuote(p.Id, p.Name, p.Price, deal.DiscountPct, deal.Promo, finalPrice);
    }
}
