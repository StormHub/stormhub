
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using ConsoleApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

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
        .ConfigureServices((builderContext, services) =>
        {
            var openAIConfig = builderContext.Configuration
                .GetSection(nameof(AzureOpenAIConfig))
                .Get<AzureOpenAIConfig>()
                ?? throw new InvalidOperationException("Azure OpenAI configuration required");

            services.AddHttpClient(nameof(AzureOpenAIConfig));
            
            services.AddTransient(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(nameof(AzureOpenAIConfig));
                
                var builder = Kernel.CreateBuilder();
                if (!string.IsNullOrEmpty(openAIConfig.APIKey))
                {
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: openAIConfig.ChatCompletionDeployment,
                        endpoint: openAIConfig.Endpoint,
                        openAIConfig.APIKey,
                        httpClient: httpClient);
                }
                else
                {
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: openAIConfig.ChatCompletionDeployment,
                        endpoint: openAIConfig.Endpoint,
                        new DefaultAzureCredential(),
                        httpClient: httpClient);
                }

                return builder.Build();
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
        var chatCompletionService = kernel.Services.GetRequiredService<IChatCompletionService>();
        
        var history = new ChatHistory();
        history.AddSystemMessage("Extract the event information.");
        history.AddUserMessage("Alice and Bob are going to a science fair on Friday.");

        var jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var responseFormat = CalendarEvent.JsonResponseSchema(jsonSerializerOptions);
        
        var responses = await chatCompletionService.GetChatMessageContentAsync(
            history, 
            new AzureOpenAIPromptExecutionSettings
            {
                ResponseFormat = responseFormat 
            }, 
            cancellationToken: lifetime.ApplicationStopping);

        var content = responses.ToString();
        if (!string.IsNullOrEmpty(content))
        {
            var result = JsonSerializer.Deserialize<CalendarEvent>(content, jsonSerializerOptions);
            Console.WriteLine($"{result?.Name}, {result?.Day}, {string.Join(", ", result?.Participants ?? [])}");
            // Prints
            // Science Fair, Friday, Alice, Bob
        }
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