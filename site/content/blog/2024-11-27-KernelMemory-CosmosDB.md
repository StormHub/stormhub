---
title: Kernel Memory with Cosmos DB for NoSQL
description: Kernel Memory with Cosmos DB for NoSQL vector search.
date: 2024-11-27
tags: .NET AI KernelMemory
permalink: ".net/ai/kernelmemory/2024/11/27/KernelMemory-CosmosDB.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

Officially announced in Microsoft Build 2024, Cosmos DB for NoSQL now support vector search. It also means [Kernel Memory](https://github.com/microsoft/kernel-memory) can be integrated with Cosmos DB for NoSQL. 

## Enable Cosmos DB for NoSQL to support vector search.
<img eleventy:ignore src="https://github.com/StormHub/stormhub/blob/main/resources/2024-11-27/azure-cosmos-db.png?raw=true" alt="Azure CosmosDB configurations">

## Implement IMemoryDb for kernel memory with cosmos client
The key is [VectorDistance](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query/vectordistance) function to match against embeddings.

```csharp
var sql =
    $"""
    SELECT Top @topN
      x.id, x.tags, x.payload, x.embedding, x.similarityScore
    FROM (
      SELECT
        c.id, c.tags, c.payload, c.embedding,VectorDistance(c.embedding, @embedding) AS similarityScore 
      FROM
        c
    ) AS x
    WHERE x.similarityScore > @similarityScore
    ORDER BY x.similarityScore desc
    """;

var queryDefinition = new QueryDefinition(sql)
    .WithParameter("@topN", limit)
    .WithParameter("@embedding", textEmbedding.Data)
    .WithParameter("@similarityScore", minRelevance);

// Index name as cosmos container name
var feedIterator = _cosmosClient
    .GetDatabase(DatabaseName)
    .GetContainer(index)
    .GetItemQueryIterator<MemoryRecordResult>(queryDefinition)
```

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-11-27/ConsoleApp)
