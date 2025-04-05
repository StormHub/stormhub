using ConsoleApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using OllamaSharp;

IHost? host = default;

try
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((builderContext, builder) =>
        {
            builder.AddJsonFile("appsettings.json", false);
            builder.AddJsonFile($"appsettings.{builderContext.HostingEnvironment.EnvironmentName}.json", true);

            if (builderContext.HostingEnvironment.IsDevelopment()) builder.AddUserSecrets<Program>();

            builder.AddEnvironmentVariables();
        })
        .ConfigureServices((_, services) =>
        {
            services.AddTransient<TraceHttpHandler>();
            services.AddHttpClient(nameof(OllamaApiClient))
                .AddHttpMessageHandler<TraceHttpHandler>().ConfigureHttpClient(client =>
                {
                    client.BaseAddress = new Uri("http://localhost:11434");
                });
            
            // Ollama
            services.AddTransient<OllamaApiClient>(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(nameof(OllamaApiClient));
                
                var client = new OllamaApiClient(httpClient, defaultModel: "phi4-mini");
                return client;
            });
            
            // MCP
            services.AddSingleton(
                new McpServerConfig
                {
                    Id = "playwright",
                    Name = "Playwright",
                    TransportType = TransportTypes.Sse,
                    Location = "http://localhost:8931"
                });
            
            // Kernel
            services.AddTransient(provider =>
            {
                var client = provider.GetRequiredService<OllamaApiClient>();
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                
                var builder = Kernel.CreateBuilder();
                builder.AddOllamaChatCompletion(client, serviceId: "local");
                builder.Services.AddSingleton(loggerFactory);
                
                return builder;
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var serverConfig = scope.ServiceProvider.GetRequiredService<McpServerConfig>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        
        var mcpClient = await McpClientFactory.CreateAsync(serverConfig, loggerFactory:loggerFactory);
        var tools = await mcpClient.ListToolsAsync();

        var builder = scope.ServiceProvider.GetRequiredService<IKernelBuilder>();
        builder.Plugins.AddFromFunctions(
            pluginName: "playwright",
            functions: tools.Select(x => x.AsKernelFunction()));
        var kernel = builder.Build();

        var executionSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                options: new()
                {
                    RetainArgumentTypes = true
                }),
            ExtensionData = new Dictionary<string, object>
            {
                { "temperature", 0 }
            }
        };

        var chatCompletionService = kernel.Services.GetRequiredKeyedService<IChatCompletionService>("local");
        var history = new ChatHistory();
        history.AddUserMessage("open browser and navigate to https://www.google.com");
        var messageContent = await chatCompletionService.GetChatMessageContentAsync(history,
            executionSettings, 
            kernel, 
            cancellationToken: lifetime.ApplicationStopping);
        Console.WriteLine(messageContent.Content);
    }

    lifetime.StopApplication();
    await host.WaitForShutdownAsync(lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    Console.WriteLine($"Host terminated unexpectedly! \n{ex}");
}
finally
{
    host?.Dispose();
}