using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Agents.Api;
using Amazon.BedrockRuntime;
using Amazon.Runtime;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
   builder.Configuration.AddUserSecrets<Program>();
}
// Configure JSON serialization
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Add(AgentJsonContext.Default);
});
builder.Services.AddAGUI();

builder.Services.AddTransient<AgentOptions>();

const string localModel = "qwen3.5:9b";
const string bedrockModel = "anthropic.claude-3-5-sonnet-20241022-v2:0";
string[] models =
[
    localModel,
    bedrockModel
];

builder.Services.AddTransient<TraceHttpHandler>();

// Ollama
builder.Services
    .AddHttpClient(localModel)
    .AddHttpMessageHandler<TraceHttpHandler>()
    .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });
builder.Services.AddKeyedTransient<IChatClient>(localModel,
    (provider, _) =>
    {
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(localModel);
        var ollamaApiClient = new OllamaApiClient(httpClient, localModel);
        return ollamaApiClient;
    });

// Bedrock
var bedrockConfig =
    builder.Configuration
        .GetSection(nameof(BedrockConfiguration))
        .Get<BedrockConfiguration>()
    ?? throw new InvalidOperationException($"AWS {nameof(BedrockConfiguration)} configuration required");
builder.Services
    .AddHttpClient(bedrockModel)
    .AddHttpMessageHandler<TraceHttpHandler>();

builder.Services.AddKeyedTransient<IChatClient>(bedrockModel,
    (provider, _) =>
    {
        var credentials = new SessionAWSCredentials(
            bedrockConfig.KeyId,
            bedrockConfig.AccessKey,
            bedrockConfig.Token);

        var regionEndpoint = bedrockConfig.RequireRegionEndpoint();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var runtimeConfig = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = regionEndpoint,
            HttpClientFactory = new BedrockHttpClientFactory(factory, bedrockModel)
        };

        var client = new AmazonBedrockRuntimeClient(credentials, runtimeConfig);
        return client.AsIChatClient();
    });

// AI Agent
foreach (var model in models)
{
    builder.Services.AddKeyedTransient<ChatClientAgent>(
        model,
        (provider, key) =>
        {
            var options = provider.GetRequiredService<AgentOptions>();
            var agentOption = options.CreateAgentOptions(key.ToString(), model);
            var agent = provider.GetRequiredKeyedService<IChatClient>(key)
                .AsAIAgent(agentOption, provider.GetRequiredService<ILoggerFactory>());
            return agent;
        });
}

var app = builder.Build();
var agent = app.Services.GetRequiredKeyedService<ChatClientAgent>(localModel);
app.MapAGUI("/", agent);

await app.RunAsync();