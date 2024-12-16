---
title: Kernel Memory with Azure AI Vision
description: Kernel Memory with Azure AI Vision.
date: 2024-11-30
tags: .NET AI KernelMemory
permalink: ".net/ai/kernelmemory/2024/11/29/KernelMemory-Azure-AI-Vision.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*


Extracting text from images for Retrieval-Augmented Generation (RAG) is a common task. [Kernel Memory](https://github.com/microsoft/kernel-memory) supports OCR functionality out of the box. 

It is less obvious that importing images requires IOcrEngine implementations for kernel memory.

Kernel Memory only comes with [Azure AI Document Intelligence](https://azure.microsoft.com/products/ai-services/ai-document-intelligence) extension suppot. Here is an example of integrating [Azure Computer Vision](https://learn.microsoft.com/en-gb/azure/ai-services/computer-vision) into [Kernel Memory](https://github.com/microsoft/kernel-memory) document ingestion.


## Create Azure Computer Vision resource.
Note different Azure regions support different visual features such as Caption. Only Read required for this sample.

## Implement IOcrEngine
It has only one method in the interface and relatively straightforward
```csharp
public async Task<string> ExtractTextFromImageAsync(Stream imageContent, CancellationToken cancellationToken = default)
{
   var imageData = await BinaryData.FromStreamAsync(imageContent, cancellationToken);
        
   var result =  await _imageAnalysisClient.AnalyzeAsync(
        imageData,
        VisualFeatures.Read, // Check regions for feature support
        new ImageAnalysisOptions { GenderNeutralCaption = true },
        cancellationToken);

    var buffer = new StringBuilder();
    if (result.HasValue)
    {
      foreach (var block in result.Value.Read.Blocks)
      {
        buffer.AppendLine();
        foreach (var line in block.Lines)
        {
          buffer.AppendLine(line.Text);
        }
      }
    }
        
    return buffer.ToString();
}
```

## Register OCR with Kernel Memory
```csharp
memoryBuilder.WithCustomImageOcr<AzureImageToText>();
```

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-11-30/ConsoleApp)


