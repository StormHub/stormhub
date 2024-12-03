---
layout: post
title:  "Microsoft extension AI"
date:   2024-10-10 12:01:02 +1000
categories: .NET AI
excerpt_separator: <!--more-->
---

Setup for Azure OpenAI with [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/).

<!--more-->

# Add Azure Open AI first

```csharp
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
```

# Setup for IChatClient
```csharp
services.AddChatClient(builder =>
    builder.Services.GetRequiredService<AzureOpenAIClient>()
    .AsChatClient("gpt-4o"));
```

# Setup for IEmbeddingGenerator
```csharp
services.AddEmbeddingGenerator<string, Embedding<float>>(builder =>
    builder.Services.GetRequiredService<AzureOpenAIClient>()
    .AsEmbeddingGenerator("text-embedding-ada-002", default));
```

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-10-10/ConsoleApp)


