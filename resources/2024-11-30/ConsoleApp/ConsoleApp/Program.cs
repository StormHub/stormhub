using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Core.Pipeline;
using Azure.Identity;
using ConsoleApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.DocumentStorage.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;

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
            
            var serviceConfig = builderContext.Configuration
                .GetSection("AzureAIServiceConfig")
                .Get<AzureAIServiceConfig>()
                ?? throw new InvalidOperationException("Azure AI service configuration required");
            
            services.AddHttpClient(nameof(ImageAnalysisClient));
            services.AddTransient<ImageAnalysisClient>(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(nameof(ImageAnalysisClient));
                var options = new ImageAnalysisClientOptions
                {
                    Transport = new HttpClientTransport(httpClient)
                };
                
                return !string.IsNullOrEmpty(serviceConfig.APIKey)
                    ? new ImageAnalysisClient(
                        serviceConfig.Endpoint,
                        new AzureKeyCredential(serviceConfig.APIKey),
                        options)
                    : new ImageAnalysisClient(
                        serviceConfig.Endpoint,
                        new DefaultAzureCredential(),
                        options);
            });
            services.AddTransient<AzureImageToText>();
                
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

                var ocrEngine = provider.GetRequiredService<AzureImageToText>();
                memoryBuilder.WithCustomImageOcr(ocrEngine);
                
                memoryBuilder.WithCustomImageOcr<AzureImageToText>();
                
                memoryBuilder
                    .WithSimpleVectorDb(SimpleVectorDbConfig.Persistent)
                    .WithSimpleFileStorage(SimpleFileStorageConfig.Persistent);

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

        const string documentId = "test";
        if (!await kernelMemory.IsDocumentReadyAsync(documentId,
                index: indexName,
                cancellationToken: lifetime.ApplicationStopping))
        {
            Console.WriteLine("Importing memories...");
            await kernelMemory.ImportDocumentAsync(
               filePath: "resources/test.png",
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