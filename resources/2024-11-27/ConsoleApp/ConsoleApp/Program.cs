using Azure;
using Azure.Identity;
using ConsoleApp;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.OpenAI;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.MemoryStorage;

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
                .Get<AzureBlobsConfig>() ?? throw new InvalidOperationException("Azure blob storage configuration required");

            var cosmosConfiguration = builderContext.Configuration
                .GetSection("AzureCosmosDb")
                .Get<AzureCosmosDbConfig>() ?? throw new InvalidOperationException("Azure cosmos configuration required");

            // CosmosDB configuration
            services.AddHttpClient(nameof(CosmosClient));
            services.AddTransient<CosmosClient>(provider =>
            {
                var options = new CosmosClientOptions
                {
                    HttpClientFactory = () =>
                    {
                        var factory = provider.GetRequiredService<IHttpClientFactory>();
                        return factory.CreateClient(nameof(CosmosClient));
                    },
                    UseSystemTextJsonSerializerWithOptions = AzureCosmosDbConfig.DefaultJsonSerializerOptions
                };
                
                var endpoint = cosmosConfiguration.Endpoint;
                var apiKey = cosmosConfiguration.APIKey;
                
                return !string.IsNullOrEmpty(apiKey) 
                    ? new CosmosClient(endpoint, new AzureKeyCredential(apiKey), options) 
                    : new CosmosClient(endpoint, new DefaultAzureCredential(), options);
            });

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
                    .WithAzureBlobsDocumentStorage(blobStorageConfiguration);

                builder.Services.AddSingleton<IMemoryDb, AzureCosmosDbMemory>();
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