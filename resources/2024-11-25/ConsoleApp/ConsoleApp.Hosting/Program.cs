using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.OpenAI;
using Microsoft.KernelMemory.Configuration;

/*
// Local development
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.KernelMemory.Pipeline.Queue.DevTools;
*/

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
                .Get<AzureOpenAIConfig>();
            var embeddingConfiguration = builderContext.Configuration
                .GetSection("AzureOpenAITextEmbeddingGeneration")
                .Get<AzureOpenAIConfig>();
  
            var blobStorageConfiguration = builderContext.Configuration
                                               .GetSection("AzureBlobsDocumentStorage")
                                               .Get<AzureBlobsConfig>()
                                           ?? throw new InvalidOperationException("Azure blob storage configuration required");
            
            var blobQueueConfiguration = builderContext.Configuration
                                               .GetSection("AzureBlobQueue")
                                               .Get<AzureQueuesConfig>()
                                           ?? throw new InvalidOperationException("Azure blob queue configuration required");

            var aiSearchConfiguration = builderContext.Configuration
                                            .GetSection("AzureAISearch")
                                            .Get<AzureAISearchConfig>()
                                        ?? throw new InvalidOperationException("Azure AI search configuration required");

            services.AddKernelMemory(builder =>
            {
                var textTokenizer = new GPT4oTokenizer();
                
                builder
                    .WithAzureOpenAITextGeneration(
                        textGenerationConfiguration,
                        textTokenizer)
                    .WithCustomTextPartitioningOptions(
                        new TextPartitioningOptions
                        {
                            MaxTokensPerLine = 128,
                            MaxTokensPerParagraph = 512,
                            OverlappingTokens = 32
                        })
                    .WithAzureOpenAITextEmbeddingGeneration(
                        embeddingConfiguration,
                        textTokenizer)
                    /*
                     // Local development
                    .WithSimpleQueuesPipeline(SimpleQueuesConfig.Persistent)
                    .WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
                    .WithSimpleFileStorage(SimpleFileStorageConfig.Persistent);
                    */
                    .WithAzureQueuesOrchestration(blobQueueConfiguration)
                    .WithAzureBlobsDocumentStorage(blobStorageConfiguration)
                    .WithAzureAISearchMemoryDb(aiSearchConfiguration);
            });
            
            services.AddDefaultHandlersAsHostedServices();
        }).Build();

    
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
            
            // Wait for import to complete
            while (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                var status = await kernelMemory.GetDocumentStatusAsync(documentId: documentId, index: indexName);
                if (status is { Completed: true })
                {
                    Console.WriteLine("Importing memories completed...");
                    break;
                }

                if (status is not null)
                {
                    Console.WriteLine("Importing memories in progress...");
                    Console.WriteLine("Steps:     " + string.Join(", ", status.Steps));
                    Console.WriteLine("Completed: " + string.Join(", ", status.CompletedSteps));
                    Console.WriteLine("Remaining: " + string.Join(", ", status.RemainingSteps));
                    Console.WriteLine();
                }
                
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        const string question = "Where is Amazon rainforest on earth?";
        Console.WriteLine($"Question: {question}");
        var response =
            await kernelMemory.AskAsync(question, indexName, cancellationToken: lifetime.ApplicationStopping);
        if (response.NoResult)
        {
            Console.WriteLine(response.NoResultReason ?? "No answer");
        }
        else
        {
            Console.WriteLine($"Answer: \n {response.Result}");
            Console.WriteLine("\n\nRelevant sources");
            foreach (var citation in response.RelevantSources)
            {
                Console.WriteLine(citation.SourceName);
                foreach (var partition in citation.Partitions)
                {
                    Console.WriteLine($" * Partition {partition.PartitionNumber}, relevance: {partition.Relevance}");
                    Console.WriteLine(partition.Text);
                }
            }
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