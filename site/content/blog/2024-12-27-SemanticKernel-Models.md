---
title: Working with multiple language models in Semantic Kernel
description: Use multiple language models in Semantic Kerneal.
date: 2024-12-27
tags: [ ".NET", "AI", "Semantic Kernel" ]
---

# {{title}}

*{{date | readableDate }}*

It is common to work with multiple large language models (LLMs) simultaneously, especially when running evaluations or tests. [Semantic Kernel](https://github.com/microsoft/semantic-kernel) supports registering multiple text generation and embedding services using serviceId and modelId.

## Register 'serviceId' and 'modelId'
Suppose we have the following setup
```csharp
 builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4-1106-Preview",
    endpoint: "https://resource-name.openai.azure.com",
    apiKey: "api-key",
    modelId: "gpt-4",
    serviceId: "azure:gpt-4");
                    
builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o-2024-08-06",
    endpoint: "https://resource-name.openai.azure.com",
    apiKey: "api-key",
    modelId: "gpt-4o",
    serviceId: "azure:gpt-4o");

 builder.AddOllamaChatCompletion(
    modelId: "phi3",
    endpoint: new Uri("http://localhost:11434"),
    serviceId: "local:phi3");
```

When execute kernel functions or prompts, 'serviceId' and 'modelId' can be passed into 'PromptExecutionSettings' like the following shows
```csharp
var promptExecutionSettings  = new PromptExecutionSettings
{
    ServiceId = "local:phi3"
};
// 
// or just modelId 
//    new PromptExecutionSettings
//     {
//         ModelId = "gpt-4o"
//     }
//
var result = await kernel.InvokePromptAsync(
    """
    Answer with the given fact:
    {{$fact}}

    input:
    {{$question}}
    """, 
    new KernelArguments(promptExecutionSettings)
    {
       ["fact"] = "Sky is blue and violets are purple",
       ["question"] = "What color is sky?"
    });
```

When registering chat completion services, if serviceId is provided, [Semantic Kernel](https://github.com/microsoft/semantic-kernel) also registers chat completion services as keyed. With the above registration, the following would work:
```csharp
var chatCompletionService = kernel.Services
    .GetRequiredKeyedService<IChatCompletionService>("azure:gpt-4o");
```

## IAIService and IAIServiceSelector
All AI-related services, including chat completion and text embedding, implement the IAIService interface, which defines a metadata property. This metadata contains attributes specific to the service implementation. For instance, the AzureOpenAIChatCompletionService includes the deployment name and model name. The default IAIServiceSelector resolves services by serviceId first, and then by modelId to match the IAIService metadata. To gain full control over AI service selection, you can implement a custom IAIServiceSelector and register it as a service with [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-12-27/ConsoleApp)
