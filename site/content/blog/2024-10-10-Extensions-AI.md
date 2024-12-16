---
title: Microsoft extension AI
description: Microsoft extension AI.
date: 2024-10-10
tags: .NET AI
permalink: ".net/ai/2024/10/10/Extensions-AI.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

[Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/)

## Setup Azure OpenAI with 
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

## Setup IChatClient
```csharp
services.AddChatClient(builder =>
    builder.Services.GetRequiredService<AzureOpenAIClient>()
    .AsChatClient("gpt-4o"));
```

## Setup IEmbeddingGenerator
```csharp
services.AddEmbeddingGenerator<string, Embedding<float>>(builder =>
    builder.Services.GetRequiredService<AzureOpenAIClient>()
    .AsEmbeddingGenerator("text-embedding-ada-002", default));
```

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-10-10/ConsoleApp)



