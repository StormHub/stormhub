---
title: Kernel Memory with Azure
description: Kernel Memory with Azure.
date: 2024-11-18
tags: .NET AI KernelMemory
permalink: ".net/ai/kernelmemory/2024/11/18/KernelMemory-Azure.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

[Kernel Memory](https://github.com/microsoft/kernel-memory) with Azure OpenAI,
Blob storage and AI Search.

## Azure Open AI
On AzureOpenAI resource, deploy gpt-4 chat completion model and text-embedding-ada-002 embedding model
```csharp
var builder = new KernelMemoryBuilder()
    .WithAzureOpenAITextGeneration(
        new AzureOpenAIConfig
        {
            Auth = AzureOpenAIConfig.AuthTypes.APIKey,
            APIKey = "Your AzureOpenAI api key",
            Endpoint = "https://your-azure-open-ai-resource-name.openai.azure.com",
            Deployment = "gpt-4"
        })
    .WithAzureOpenAITextEmbeddingGeneration(
        new AzureOpenAIConfig
        {
            Auth = AzureOpenAIConfig.AuthTypes.APIKey,
            APIKey = "Your AzureOpenAI api key",
            Endpoint = "https://your-azure-open-ai-resource-name.openai.azure.com",
            Deployment = "text-embedding-ada-002"
        });
```

## Azure storage account 
Azure blob storage to store kenerl memory pipeline artifacts
```csharp
var builder = new KernelMemoryBuilder()
    .WithAzureBlobsDocumentStorage(
        new AzureBlobsConfig
        {
            Account = "your-blob-storage-account",
            Auth = AzureBlobsConfig.AuthTypes.AccountKey,
            AccountKey = "your-blob-account-key",
            Container = "document-ingestion"
        })
```

# Azure AI Search service
Azure AI search service as vector databases
```csharp
var builder = new KernelMemoryBuilder()
    .WithAzureAISearchMemoryDb(
        new AzureAISearchConfig
        {
            Endpoint = "https://your-search-service-resource-name.search.windows.net",
            Auth = AzureAISearchConfig.AuthTypes.APIKey,
            APIKey = "your search service api key"
        })
```

## Import some document and ask questions
```csharp
await kernelMemory.ImportDocumentAsync(
    filePath: "resources/earth_book_2019_tagged.pdf",
    documentId: "earth_book_2019",
    index: "books");

var response =
    await kernelMemory.AskAsync(
        "Where is Amazon rainforest on earth?", 
        index: "books");    
   
```
Note the index name "books", kernel memory automatically creates Azure AI Search index name "books" if it does not exist and "books" folder in the blob container.

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-11-18/ConsoleApp)


