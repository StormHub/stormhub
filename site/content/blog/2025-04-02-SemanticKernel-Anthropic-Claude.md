---
title: AWS Bedrock anthropic claude tool call integration with microsoft semantic kernel
description: Microsoft semantic kernel integration with WS Bedrock anthropic claude with function calls
date: 2025-04-02
tags: [ ".NET", "AI", "Semantic Kernel", 'Anthropic' ]
---

# {{title}}

*{{date | readableDate }}*

As of April 2025, the official Microsoft Semantic Kernel connector for Amazon (Microsoft.SemanticKernel.Connectors.Amazon) [https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.Amazon/1.36.1-alpha] does not natively support tool/function calls. Apparently, Semantic Kernel is shifting its approach towards an LLM abstraction layer based on Microsoft.Extensions.AI [https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI], aiming for a more unified and extensible architecture. Currently, only OpenAI and Ollama implementations are available within this new abstraction. It is anticipated that an implementation for AWS Bedrock Anthropic Claude based on Microsoft.Extensions.AI will become available in the future. Therefore, in the interim, I implemented a custom solution. The approach leverages the existing `IChatClient` interface [https://github.com/dotnet/extensions/blob/68b25aeb2d752273e1d5621b38a7869ce63970c3/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/IChatClient.cs], making the implementation relatively straightforward. Since function calls are supported by this interface, the solution involves implementing it on top of the AWS Bedrock Runtime SDK [https://www.nuget.org/packages/AWSSDK.BedrockRuntime/4.0.0-preview.13].

## Implement IChatClient with AWS Bedrock Runtime

The `IChatClient` interface essentially contains two methods: one for standard chat responses and another for streamed responses. The implementation involves mapping these two methods to the `IAmazonBedrockRuntime.ConverseAsync` and `ConverseStreamAsync` methods, as demonstrated in the full implementation of the `AnthropicChatClient` [https://github.com/StormHub/stormhub/tree/main/resources/2025-04-02/ConsoleApp/AnthropicChatClient.cs].

## Setting up Function Calls with Semantic Kernel

Here's how to set up function calls with Semantic Kernel using our custom `AnthropicChatClient`:

1.  **Set up kernel and functions**
    This step configures the chat completion service with function invocation capabilities and registers it with the Semantic Kernel.

    ```csharp
    // Set up chat completion service
    IChatClient chatClient = ...;
    IChatCompletionService chatService =
        chatClient
            .AsBuilder()
            .UseFunctionInvocation() // Enables function call functionality
            .Build()
            .AsChatCompletionService();

    // Register the Bedrock chat completion service
    var builder = Kernel.CreateBuilder();
    builder.Services.AddKeyedSingleton("bedrock", chatService);
    // Add plugins/functions
    builder.Plugins.AddFromType<MenuPlugin>();
    // ...
    var kernel = builder.Build();
    ```

2.  **Use automatically tool calls**
    This code demonstrates how to use the configured chat completion service to automatically invoke functions based on the user's input.

    ```csharp
    // Set up bedrock
    var runtimeClient = new AmazonBedrockRuntimeClient(RegionEndpoint.APSoutheast2);
    IChatClient client = new AnthropicChatClient(runtimeClient, "anthropic.claude-3-5-sonnet-20241022-v2:0");

    // Configure the chat client as shown in step 1.
    IChatCompletionService chatCompletionService = client
        .AsBuilder()
        .UseFunctionInvocation()
        .Build()
        .AsChatCompletionService();

    var chatHistory = new ChatHistory();
    chatHistory.AddUserMessage("What is the special soup and its price?");

    var promptExecutionSettings = new PromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new()
        {
            RetainArgumentTypes = true
        }),
        ExtensionData = new Dictionary<string, object>
        {
            { "temperature", 0 }, 
            { "max_tokens_to_sample", 1024 } // Required parameter for Anthropic models
        }
    };

    var messageContent = await chatCompletionService
        .GetChatMessageContentAsync(chatHistory,  promptExecutionSettings, kernel);
    Console.WriteLine(messageContent.Content);

    // Expected output : Today's special soup is Clam Chowder and it costs $9.99.
    ```

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-04-02/ConsoleApp).