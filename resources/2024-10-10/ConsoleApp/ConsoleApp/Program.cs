using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost? host = default;
try
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((builderContext, builder) =>
            {
                if (builderContext.HostingEnvironment.IsDevelopment())
                {
                    builder.AddUserSecrets<Program>();
                }
                builder.AddEnvironmentVariables();
            })
        .ConfigureServices((builderContext, services) =>
            {
                var uri = builderContext.Configuration["AZURE_OPENAI_ENDPOINT"] 
                          ?? throw new InvalidOperationException("Azure OpenAI endpoint url required");
                
                var modelId = builderContext.Configuration["MODEL_ID"]
                              ?? throw new InvalidOperationException("Azure OpenAI model id required");
                var key = builderContext.Configuration["AZURE_OPENAI_KEY"] 
                          ?? throw new InvalidOperationException("Azure OpenAI api key required");
                
                var embeddingId = builderContext.Configuration["EMBEDDING_ID"] 
                          ?? throw new InvalidOperationException("Azure OpenAI embedding model required");
                services.AddHttpClient(nameof(AzureOpenAIClient));

                services.AddTransient<AzureOpenAIClient>(provider =>
                {
                    var factory = provider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(nameof(AzureOpenAIClient));
                    var clientOptions = new AzureOpenAIClientOptions
                    {
                        Transport = new HttpClientPipelineTransport(httpClient)
                    };
                    
                    return new AzureOpenAIClient(new Uri(uri), new AzureKeyCredential(key), clientOptions);
                });

                services.AddChatClient(provider =>
                    provider.GetRequiredService<AzureOpenAIClient>()
                        .AsChatClient(modelId));

                services.AddEmbeddingGenerator(provider =>
                    provider.GetRequiredService<AzureOpenAIClient>()
                        .AsEmbeddingGenerator(embeddingId));
            })
        .Build();

    await host.StartAsync();

    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    
    await using var scope = host.Services.CreateAsyncScope();
    using (var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>())
    {
        var response = await chatClient.CompleteAsync("Hello, who are you?", default, lifetime.ApplicationStopping);
        Console.WriteLine(response.Message);
    }

    using (var generator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator<string,Embedding<float>>>())
    {
        var embeddings = await generator.GenerateAsync([ "Sky is blue and violet is purple"], default, lifetime.ApplicationStopping);
        Console.WriteLine(string.Join(", ", embeddings[0].Vector.ToArray()));
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