
using Azure.Identity;
using ConsoleApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

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
                        deploymentName: "gpt-4",
                        endpoint: openAIConfig.Endpoint,
                        apiKey: openAIConfig.APIKey,
                        modelId: "gpt-4",
                        serviceId: "azure:gpt-4",
                        httpClient: httpClient);
                    
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: "gpt-4o",
                        endpoint: openAIConfig.Endpoint,
                        openAIConfig.APIKey,
                        modelId: "gpt-4o",
                        serviceId: "azure:gpt-4o",
                        httpClient: httpClient);
                }
                else
                {
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: "gpt-4",
                        endpoint: openAIConfig.Endpoint,
                        new DefaultAzureCredential(),
                        modelId: "gpt-4",
                        serviceId: "azure:gpt-4",
                        httpClient: httpClient);
                    
                    builder.AddAzureOpenAIChatCompletion(
                        deploymentName: "gpt-4o",
                        endpoint: openAIConfig.Endpoint,
                        new DefaultAzureCredential(),
                        modelId: "gpt-4o",
                        serviceId: "azure:gpt-4o",
                        httpClient: httpClient);
                }
                
                builder.AddOllamaChatCompletion(
                    modelId: "phi3",
                    endpoint: new Uri("http://localhost:11434"),
                    serviceId: "local:phi3");
                
                return builder.Build();
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        var chatCompletionService = kernel.Services.GetRequiredKeyedService<IChatCompletionService>("azure:gpt-4");
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage("Hello, who are you?");
        var response =
            await chatCompletionService.GetChatMessageContentAsync(chatHistory,
                cancellationToken: lifetime.ApplicationStopping);
        Console.WriteLine($"[{response.ModelId}] {response.Content}");

        var prompt = 
            """
            Answer with the given fact:
            {{$fact}}
            
            input:
            {{$question}}
            """;
        
        var result = await kernel.InvokePromptAsync(prompt, 
            new KernelArguments(
                new PromptExecutionSettings
                {
                    ServiceId = "azure:gpt-4o"
                })
            {
                ["fact"] = "Sky is blue and violets are purple",
                ["question"] = "What color is sky?"
            });
        WriteResult(result);
        
        result = await kernel.InvokePromptAsync(prompt, new KernelArguments(
            new PromptExecutionSettings
            {
                ModelId = "gpt-4"
            })
        {
            ["fact"] = "Sky is blue and violets are purple",
            ["question"] = "What color is sky?"
        });
        WriteResult(result);

        void WriteResult(FunctionResult functionResult)
        {
            var content = functionResult.GetValue<ChatMessageContent>();
            Console.WriteLine($"[{content?.ModelId}] {content?.Content}");
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