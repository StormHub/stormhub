using System.Text.RegularExpressions;

namespace Api.Data;

public sealed record Product(string Id, string Name, string Category, decimal Price, IReadOnlyList<string> Tags);

public sealed record Review(double Rating, int Count, IReadOnlyList<string> Highlights);

/// <param name="DiscountPct">Percentage off the list price, 0 if none.</param>
public sealed record Deal(int DiscountPct, string? Promo = null);

public sealed record CategoryCount(string Category, int Count);

/// <summary>
/// Search and lookup over mock in-memory shopping data. Deterministic, no external services.
/// Ported from agent-orchestrator/src/server/data/catalog.ts. Register with DI and inject where needed.
/// </summary>
public sealed partial class Catalog
{
    public static readonly IReadOnlyList<Product> Products =
    [
        new("p_laptop_dell", "Dell XPS 13", "laptop", 999, ["laptop", "ultrabook", "work"]),
        new("p_laptop_mac", "MacBook Air M3", "laptop", 1199, ["laptop", "apple", "work"]),
        new("p_phone_iphone15", "iPhone 15", "phone", 799, ["phone", "apple", "ios"]),
        new("p_phone_pixel", "Pixel 8", "phone", 699, ["phone", "google", "android"]),
        new("p_shoes_run", "Nike Pegasus 41 Running Shoes", "shoes", 130, ["shoes", "running", "nike"]),
        new("p_shoes_trail", "Salomon Trail Runners", "shoes", 150, ["shoes", "running", "trail"]),
        new("p_headphones", "Sony WH-1000XM5", "audio", 349, ["headphones", "audio", "noise-cancelling"]),
    ];

    /// <summary>product id -> units in stock</summary>
    public static readonly IReadOnlyDictionary<string, int> Stock = new Dictionary<string, int>
    {
        ["p_laptop_dell"] = 12,
        ["p_laptop_mac"] = 0,
        ["p_phone_iphone15"] = 34,
        ["p_phone_pixel"] = 5,
        ["p_shoes_run"] = 80,
        ["p_shoes_trail"] = 0,
        ["p_headphones"] = 21,
    };

    /// <summary>product id -> active deal</summary>
    public static readonly IReadOnlyDictionary<string, Deal> Deals = new Dictionary<string, Deal>
    {
        ["p_laptop_dell"] = new(10, "Summer Sale"),
        ["p_laptop_mac"] = new(0),
        ["p_phone_iphone15"] = new(5, "Trade-in bonus"),
        ["p_phone_pixel"] = new(0),
        ["p_shoes_run"] = new(20, "Clearance"),
        ["p_shoes_trail"] = new(0),
        ["p_headphones"] = new(15, "Summer Sale"),
    };

    /// <summary>product id -> reviews</summary>
    public static readonly IReadOnlyDictionary<string, Review> Reviews = new Dictionary<string, Review>
    {
        ["p_laptop_dell"] = new(4.5, 1280, ["Great keyboard", "Excellent battery life"]),
        ["p_laptop_mac"] = new(4.7, 2100, ["Silent and fast", "Premium build"]),
        ["p_phone_iphone15"] = new(4.6, 5400, ["Smooth performance", "Good cameras"]),
        ["p_phone_pixel"] = new(4.4, 1900, ["Best-in-class camera", "Clean Android"]),
        ["p_shoes_run"] = new(4.6, 3200, ["Comfortable for long runs", "Durable"]),
        ["p_shoes_trail"] = new(4.3, 540, ["Great grip", "Runs small"]),
        ["p_headphones"] = new(4.8, 4100, ["Top-tier noise cancelling", "Comfortable"]),
    };

    /// <summary>Filler words stripped from a search query before matching.</summary>
    public static readonly IReadOnlySet<string> Stopwords = new HashSet<string>
    {
        "show", "me", "find", "get", "search", "for", "the", "a", "an", "any", "some",
        "in", "of", "stock", "please", "see", "want", "need", "looking", "like", "with",
        "do", "you", "have", "got", "available", "sell", "selling", "your", "is", "are",
    };

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    private static IEnumerable<string> Tokenize(string text) =>
        NonAlphanumeric().Split(text.ToLowerInvariant()).Where(t => t.Length > 0);

    /// <summary>Tokenize a query: split on non-alphanumerics, drop stopwords, crudely singularize.</summary>
    private static List<string> SearchTerms(string query) =>
        Tokenize(query)
            .Where(t => !Stopwords.Contains(t))
            .Select(t => t.Length > 3 && t.EndsWith('s') ? t[..^1] : t)
            .ToList();

    /// <summary>
    /// Find products by free-text query (keyword or category) and optional max price.
    /// Keyword match over name + category + tags.
    /// </summary>
    public IReadOnlyList<Product> Search(string? query, decimal? maxPrice = null)
    {
        var terms = SearchTerms(query ?? string.Empty);
        return Products.Where(p =>
        {
            if (maxPrice is not null && p.Price > maxPrice) return false;
            if (terms.Count == 0) return true;
            // Match on whole words (equality or prefix) so e.g. "phone" doesn't match
            // "headphones". Prefix keeps plurals/stems working ("shoe" -> "shoes").
            var tokens = Tokenize($"{p.Name} {p.Category} {string.Join(' ', p.Tags)}").ToList();
            return terms.Any(t => tokens.Any(tok => tok.StartsWith(t, StringComparison.Ordinal)));
        }).ToList();
    }

    /// <summary>The distinct product categories with how many products are in each.</summary>
    public IReadOnlyList<CategoryCount> ListCategories() =>
        Products.GroupBy(p => p.Category).Select(g => new CategoryCount(g.Key, g.Count())).ToList();

    /// <summary>Find a product by its exact id.</summary>
    public Product? GetProduct(string productId) =>
        Products.FirstOrDefault(p => p.Id == productId);

    /// <summary>Resolve a product from an explicit id, a name, or a free-text query.</summary>
    public Product? ResolveProduct(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        return Products.FirstOrDefault(p => p.Id == reference)
            ?? Products.FirstOrDefault(p => string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase))
            ?? Search(reference).FirstOrDefault();
    }
}
