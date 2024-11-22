using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.AI.OpenAI;
using Microsoft.KernelMemory.Configuration;

/*
// Local file storage and in memory vector database for debug purposes
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
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

            var aiSearchConfiguration = builderContext.Configuration
                .GetSection("AzureAISearch")
                .Get<AzureAISearchConfig>()
                ?? throw new InvalidOperationException("Azure AI search configuration required");

            services.AddHttpClient(nameof(AzureOpenAIClient));
            services.AddSingleton<ITextTokenizer, GPT4oTokenizer>();

            services.AddTransient<IKernelMemoryBuilder>(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(nameof(AzureOpenAIClient));

                var textTokenizer = provider.GetRequiredService<ITextTokenizer>();

                var memoryBuilder = new KernelMemoryBuilder()
                    .WithAzureOpenAITextGeneration(
                        textGenerationConfiguration,
                        textTokenizer,
                        httpClient)
                    .WithCustomTextPartitioningOptions(
                        new TextPartitioningOptions
                        {
                            MaxTokensPerLine = 128,
                            MaxTokensPerParagraph = 512,
                            OverlappingTokens = 32
                        })
                    .WithAzureOpenAITextEmbeddingGeneration(
                        embeddingConfiguration,
                        textTokenizer,
                        default,
                        false,
                        httpClient);

                /*
                // Local file storage and in memory vector database for debug purposes
                if (builderContext.HostingEnvironment.IsDevelopment())
                {
                    memoryBuilder
                        .WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
                        .WithSimpleFileStorage(SimpleFileStorageConfig.Persistent);
                }
                */

                memoryBuilder
                    .WithAzureBlobsDocumentStorage(blobStorageConfiguration)
                    .WithAzureAISearchMemoryDb(aiSearchConfiguration);

                return memoryBuilder;
            });
        })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    const string indexName = "books";
    
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var builder = scope.ServiceProvider.GetRequiredService<IKernelMemoryBuilder>();
        var kernelMemory = builder.Build();

        const string documentId = "earth_book_2019";
        if (!await kernelMemory.IsDocumentReadyAsync(documentId,
                index: indexName,
                cancellationToken: lifetime.ApplicationStopping))
        {
            Console.WriteLine("Importing memories...");
            await kernelMemory.ImportDocumentAsync(
               filePath: "resources/earth_book_2019_tagged.pdf",
               documentId: documentId,
               index: indexName,
                cancellationToken: lifetime.ApplicationStopping);
        }

        const string question = "Where is Amazon rainforest on earth?";
        Console.WriteLine($"Question: {question}");
        var response =
            await kernelMemory.AskAsync(question, index: indexName, cancellationToken: lifetime.ApplicationStopping);
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