using System.Text.Json;
using System.Text.Json.Serialization;
using Agents.WebApi;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddAGUI();

// Http client
builder.Services
    .AddHttpClient(nameof(OllamaApiClient))
    .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });

// Ollama
builder.Services.AddTransient<IChatClient>(provider =>
{
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient(nameof(OllamaApiClient));
    var ollamaApiClient = new OllamaApiClient(httpClient, "phi4");
    return new OllamaChatClient(ollamaApiClient);
});

// AI Agent
builder.Services.AddKeyedTransient<ChatClientAgent>(
    "local-ollama-agent",
    (provider, key) =>
    {
        var agentOption = new ChatClientAgentOptions
        {
            Id = key.ToString(),
            Name = "Local Assistant",
            Description = "An AI agent on local Ollama.",
            ChatOptions = new ChatOptions
            {
                Temperature = 0
            }
        };

        var agent = provider.GetRequiredService<IChatClient>()
            .CreateAIAgent(agentOption, provider.GetRequiredService<ILoggerFactory>());
        return agent;
    });
var app = builder.Build();

var agent = app.Services.GetRequiredKeyedService<ChatClientAgent>("local-ollama-agent");
app.MapAGUI("/", agent);

await app.RunAsync();