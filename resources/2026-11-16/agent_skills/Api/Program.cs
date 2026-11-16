using Api.Data;
using Api.Mcp;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Catalog>();
builder.Services.AddSingleton<Pricing>();

// MCP server that serves the agent skills in ./Skills (Skills extension, io.modelcontextprotocol/skills).
// Each subdirectory with a SKILL.md is published at skill://{name}/SKILL.md via skills/list and skills/get,
// and its files are served through resources/read. skill://index.json is also served for clients that
// discover skills from that index instead of skills/list.
var skillPackages = McpServerSkillPackage
    .CreateFromSubdirectories(Path.Combine(AppContext.BaseDirectory, "Skills"));

builder.Services
    .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "Shopping", Version = "1.0.0" })
    .WithHttpTransport()
    .WithResources([SkillIndex.CreateResource(skillPackages)])
    .WithSkillPackages(skillPackages);

var app = builder.Build();

// GET /catalog?q=laptop&maxPrice=1000
app.MapGet("/catalog", (Catalog catalog, string? q, decimal? maxPrice) => catalog.Search(q, maxPrice));

// GET /pricing/p_laptop_dell
app.MapGet("/pricing/{productId}", (Pricing pricing, string productId) =>
    pricing.GetDeals(productId) is { } quote
        ? Results.Ok(quote)
        : Results.NotFound(new { error = $"Product '{productId}' not found." }));

// MCP endpoint (Streamable HTTP): /mcp
app.MapMcp("/mcp");

await app.RunAsync();
