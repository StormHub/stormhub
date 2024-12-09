using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using ConsoleApp.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage.Disk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.ML.Tokenizers;

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
            var textGenerationConfiguration = builderContext.Configuration
                .GetSection("AzureOpenAITextGeneration")
                .Get<AzureOpenAIConfig>()
                ?? throw new ConfigurationException("Azure OpenAI text generation configuration required.");
            var embeddingConfiguration = builderContext.Configuration
                .GetSection("AzureOpenAITextEmbeddingGeneration")
                .Get<AzureOpenAIConfig>()
                ?? throw new ConfigurationException("Azure OpenAI text embedding generation configuration required.");
  
            services.AddKernelMemory(builder =>
            {
                builder
                    .WithAzureOpenAITextGeneration(textGenerationConfiguration)
                    .WithCustomTextPartitioningOptions(
                        new TextPartitioningOptions
                        {
                            MaxTokensPerLine = 128,
                            MaxTokensPerParagraph = 512,
                            OverlappingTokens = 32
                        })
                    .WithAzureOpenAITextEmbeddingGeneration(embeddingConfiguration)
                    .WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
                    .WithSimpleFileStorage(SimpleFileStorageConfig.Persistent);
            });

            services.AddHttpClient(nameof(AzureOpenAIClient));
            services.AddTransient(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(nameof(AzureOpenAIClient));
                var clientOptions = new AzureOpenAIClientOptions
                {
                    Transport = new HttpClientPipelineTransport(httpClient)
                };

                return !string.IsNullOrEmpty(textGenerationConfiguration.APIKey)
                    ? new AzureOpenAIClient(
                        new Uri(textGenerationConfiguration.Endpoint),
                        new AzureKeyCredential(textGenerationConfiguration.APIKey),
                        clientOptions)
                    : new AzureOpenAIClient(
                        new Uri(textGenerationConfiguration.Endpoint),
                        new DefaultAzureCredential(),
                        clientOptions);
            });

            services.AddTransient(provider =>
            {
                var openAIClient = provider.GetRequiredService<AzureOpenAIClient>();
                
                var chatClient = openAIClient.AsChatClient(textGenerationConfiguration.Deployment);
                var tokenizer = TiktokenTokenizer.CreateForModel(textGenerationConfiguration.Deployment);
                return new ChatConfiguration(chatClient, tokenizer.ToTokenCounter(6000));
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    const string indexName = "books";
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var kernelMemory = scope.ServiceProvider.GetRequiredService<IKernelMemory>();

        const string documentId = "earth_book_2019";
        if (!await kernelMemory.IsDocumentReadyAsync(documentId,
                indexName,
                lifetime.ApplicationStopping))
        {
            Console.WriteLine("Start importing memories...");
            await kernelMemory.ImportDocumentAsync(
                "resources/earth_book_2019_tagged.pdf",
                documentId,
                index: indexName,
                cancellationToken: lifetime.ApplicationStopping);
        }

        const string question = "Where is Amazon rainforest on earth?";
        var response =
            await kernelMemory.AskAsync(question, indexName, cancellationToken: lifetime.ApplicationStopping);
        
        var chatConfiguration = scope.ServiceProvider.GetRequiredService<ChatConfiguration>();
        var answerEvaluator = new FactEvaluator();

        var reportConfiguration = DiskBasedReportingConfiguration.Create(
            storageRootPath: "./reports",
            chatConfiguration: chatConfiguration,
            evaluators:
            [
                answerEvaluator
            ],
            executionName: documentId);
        await using var scenario = await reportConfiguration.CreateScenarioRunAsync(indexName);
        var evalResult = await scenario.EvaluateAsync(
            messages:[
                new ChatMessage(ChatRole.User, question)
            ],
            modelResponse: new ChatMessage(ChatRole.Assistant, response.Result),
            additionalContext: [new FactEvaluator.EvaluationExpert("Brazil and Bolivia")]);

        foreach (var metric in evalResult.Metrics)
        {
            Console.WriteLine($"{metric.Key} {metric.Value.Interpretation?.Rating.ToString()}");
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