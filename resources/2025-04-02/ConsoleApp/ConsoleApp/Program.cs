using Amazon.BedrockRuntime;
using Amazon.Runtime;
using ConsoleApp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            var bedrockConfig = builderContext.Configuration
                .GetSection(nameof(BedrockConfiguration))
                .Get<BedrockConfiguration>()
                ?? throw new InvalidOperationException($"AWS {nameof(BedrockConfiguration)} configuration required");

            // services.AddTransient<TraceHttpHandler>();
            services.AddHttpClient(nameof(AmazonBedrockRuntimeClient));
            //    .AddHttpMessageHandler<TraceHttpHandler>();
            
            // AWS bedrock
            services.AddTransient<IAmazonBedrockRuntime>(provider =>
            {
                var credentials = new SessionAWSCredentials(
                    awsAccessKeyId: bedrockConfig.KeyId,
                    awsSecretAccessKey: bedrockConfig.AccessKey,
                    token: bedrockConfig.Token);

                var regionEndpoint = bedrockConfig.RequireRegionEndpoint();
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                
                var runtimeConfig = new AmazonBedrockRuntimeConfig
                {
                    RegionEndpoint = regionEndpoint,
                    HttpClientFactory = new BedrockHttpClientFactory(factory, nameof(AmazonBedrockRuntimeClient))
                };
                
                var client = new AmazonBedrockRuntimeClient(credentials, runtimeConfig);
                return client;
            });
            
            // Kernel
            services.AddTransient(provider =>
            {
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                var bedrockRuntime = provider.GetRequiredService<IAmazonBedrockRuntime>();
                var client = new AnthropicChatClient(bedrockRuntime, bedrockConfig.ModelId);

                var chatCompletionService = client
                    .AsBuilder()
                    .UseFunctionInvocation(loggerFactory)
                    .UseLogging(loggerFactory)
                    .Build(provider)
                    .AsChatCompletionService();
                
                var builder = Kernel.CreateBuilder();
                builder.Services.AddKeyedSingleton("bedrock", chatCompletionService);
                builder.Services.AddSingleton(loggerFactory);
                builder.Plugins.AddFromType<MenuPlugin>();
                
                return builder;
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var builder = scope.ServiceProvider.GetRequiredService<IKernelBuilder>();
        var kernel = builder.Build();
        
        var chatCompletionService = kernel.Services.GetRequiredKeyedService<IChatCompletionService>("bedrock");

        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage("What is the special soup and its price?");
        
        var promptExecutionSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new()
            {
                RetainArgumentTypes = true
            }),
            ExtensionData = new Dictionary<string, object>
            {
                { "temperature", 0 },
                { "max_tokens_to_sample", 1024 } // Required for Anthropic
            }
        };

        /*
        // Streaming
        await foreach (var chatUpdate in chatCompletionService
            .GetStreamingChatMessageContentsAsync(chatHistory, promptExecutionSettings, kernel, lifetime.ApplicationStopping))
        {
            Console.Write(chatUpdate.Content);
        }
        */

        var messageContent = await chatCompletionService.GetChatMessageContentAsync(chatHistory,
            promptExecutionSettings,
            kernel,
            lifetime.ApplicationStopping);

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