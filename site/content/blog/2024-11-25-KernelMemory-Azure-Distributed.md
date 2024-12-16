---
title: Kernel Memory document ingestion
description: Scale Kernel Memory document ingestion with Azure
date: 2024-11-25
tags: .NET AI KernelMemory
permalink: ".net/ai/kernelmemory/2024/11/25/KernelMemory-Azure-Distributed.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

Benifits of scaling document ingestion with [Kernel Memory](https://github.com/microsoft/kernel-memory) on Azure

- Scalability: Easily handle large volumes of documents by distributing the workload across multiple nodes.
- Efficiency: Process documents in parallel, reducing the overall time required for ingestion.
- Fault Tolerance: Ensure reliability and availability by distributing tasks, so if one node fails, others can take over.
- Resource Optimization: Utilize resources more effectively by balancing the load across the system.
- Flexibility: Adapt to varying workloads and scale up or down as needed.

# Setup distributed pipeline ingestion with Azure Queue Storage
```csharp
var builder = new KernelMemoryBuilder()
     .WithAzureQueuesOrchestration(
        new AzureQueuesConfig
        {
            Account = "your-blob-storage-account",
            // Or AuzreIdentity
            Auth = AzureQueuesConfig.AuthTypes.AccountKey,
            AccountKey = "your-blob-account-key"
        })
```
Once queue orchestration is registered, Kernel Memory automatically sets up DistributedPipelineOrchestrator.

# Make sure pipeline handler are hosted services.
Add handlers as hosted service to start listen to messages
```csharp
// Add handlers as hosted services
services.AddDefaultHandlersAsHostedServices();
```

# Import documents asynchronously
Distributed ingestion also makes importing document asynchronous, meaning when ImportDocumentAsync returns, the document ingestion is enqueued to be processed. 
```csharp
await kernelMemory.ImportDocumentAsync(
    filePath: "resources/earth_book_2019_tagged.pdf",
    documentId: "earth_book_2019",
    index: "books");

// Polling for status
var status = await kernelMemory.GetDocumentStatusAsync(documentId: documentId, index: indexName);
if (status is { Completed: true })
{
    Console.WriteLine("Importing memories completed...");
    break;
}
```

It is also worth noting each of the pipeline step has independant queue/posion queue on Azure Queue Storage.

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-11-25/ConsoleApp)


