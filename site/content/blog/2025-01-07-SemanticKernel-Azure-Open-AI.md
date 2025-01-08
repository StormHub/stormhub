---
title: Azure OpenAI Error Handling in Semantic Kernel
description: Handling Azure OpenAI Error Handling like rate limit in Semantic Kerneal.
date: 2025-01-07
tags: [ ".NET", "AI", "Semantic Kernel" ]
---

# {{title}}

*{{date | readableDate }}*

In real-world systems, it's crucial to handle HTTP errors effectively, especially when interacting with Large Language Models (LLMs) like Azure OpenAI. Rate limit exceeded errors (tokens per minute or requests per minute) always happen at some point, resulting in 429 errors. This blog post explores different approaches to HTTP error handling with semantic kernel and Azure OpenAI.

## Default
```csharp
var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o-2024-08-06",
    endpoint: "https://resource-name.openai.azure.com",
    apiKey: "api-key"); // Or DefaultAzureCredential
```
The default setup for [Semantic Kernel](https://github.com/microsoft/semantic-kernel) with Azure OpenAI by AddAzureOpenAIChatCompletion. This approach offers a built-in retry policy that automatically retries requests up to three times with exponential backoff. Additionally, it can detect specific HTTP headers like 'retry-after' to implement more tailored retries.

## HttpClient
```csharp
var factory = provider.GetRequiredService<IHttpClientFactory>();
var httpClient = factory.CreateClient("auzre:gpt-4o");

var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o-2024-08-06",
    endpoint: "https://resource-name.openai.azure.com",
    apiKey: "api-key",  // Or DefaultAzureCredential
    httpClient: httpClient);
```
By configuring an HttpClient instance, you can gain more control over HTTP error handling. [Semantic Kernel](https://github.com/microsoft/semantic-kernel) disables the default retry policy when HttpClient is provided. This allows you to implement custom retry logic using the Microsoft.Extensions.Http.Resilience library. With this approach, you can define the number of retry attempts, timeouts, and how to handle specific error codes like 429 (rate limit exceeded).
```csharp
services.AddHttpClient("auzre:gpt-4o")
    .AddStandardResilienceHandler() // Automatically handle transient errors inlcuding '429'
    .Configure(options =>
        {
            // Options for attempts and time out etc
            options.Retry.MaxRetryAttempts = 5;
        });
```
An important benefit of using HttpClient is that it's not limited to Azure OpenAI. This approach works with other AI connectors like OpenAI as well.

## AzureOpenAIClient
```csharp
var azureOpenAIClient = new AzureOpenAIClient(
    endpoint: new Uri("https://resource-name.openai.azure.com"),
    new ApiKeyCredential("api-key"), // Or DefaultAzureCredential
    new AzureOpenAIClientOptions());

var builder = Kernel.CreateBuilder();
builder.AddAzureOpenAIChatCompletion(
    deploymentName: "gpt-4o-2024-08-06",
    azureOpenAIClient);
```
This approach offers similar functionality to the default setup with the built-in retry policy. In addition, AzureOpenAIClient provides more flexibility from AzureOpenAIClientOptions.
```csharp
var clientOptions = new AzureOpenAIClientOptions
    {
        Transport = new HttpClientPipelineTransport(httpClient),
        RetryPolicy = new ClientRetryPolicy(maxRetries: 5)
    };
```
This configuration enables you to combine HTTP retry policies from HttpClient with custom pipeline policy-based retries from the Azure OpenAI SDK.


## Recommendations
The default setup might not be suitable for scenarios where you frequently encounter token limit issues.
Using HttpClient provides more control and flexibility, making it a favorable option for broader compatibility beyond Azure OpenAI.
If you already have AzureOpenAIClient registered and require maximum control, this approach allows you to leverage both HTTP client policies and Azure OpenAI pipeline policy-based retries.
