---
title: AWS Bedrock anthropic claude tool call integration with microsoft semantic kernel
description: Microsoft semantic kernel integration with WS Bedrock anthropic claude with function calls
date: 2025-04-02
draft: true
tags: [ ".NET", "AI", "Semantic Kernel", 'Anthropic' ]
---

# {{title}}

*{{date | readableDate }}*

As of Apr 2025, official Microsoft [Semantic Kernel](https://github.com/microsoft/semantic-kernel) connector (Microsoft.SemanticKernel.Connectors.Amazon)[https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.Amazon/1.36.1-alpha] does not support tool/function calls out of the box. Apparently, [Semantic Kernel](https://github.com/microsoft/semantic-kernel) is shifting it way towards (Microsoft.Extensions.AI)[https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI] based LLM abstraction layer. There are only two implementations available OpenAI and Ollama.  I am reasonably certain (Microsoft.Extensions.AI)[https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI] based implementation for AWS bedrock anthropic claude is going to be available at some point in the near future. In the meantime, I was required to integrate with AWS bedrock anthropic claude. Therefore I have to implement it myself in the interm. The approach is actually relatively simple. Since function calls is supported by [IChatClient](https://github.com/dotnet/extensions/blob/68b25aeb2d752273e1d5621b38a7869ce63970c3/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/IChatClient.cs), all we have to is to implment it based on top of [AWS bedrock runtime](https://www.nuget.org/packages/AWSSDK.BedrockRuntime/4.0.0-preview.13).

## Implement IChatClient with AWS bedrock runtime
There are basically two methods in the interface one is for chat reponse and the other one is for streamed. All we need to do is to map those two against IAmazonBedrockRuntime ConverseAsync and ConverseStreamAsync methods, full implemenation of [AnthropicChatClient](https://github.com/StormHub/stormhub/tree/main/resources/2025-04-02/ConsoleApp/AnthropicChatClient.cs).


## Setting up the function calls with semantic kernel

1.  **Setup kernel and functions**
    ```csharp
    // Set up chat completion service
    IChatClient chatClient = ...;
    IChatCompletionService chatService = 
        chatClient
            .AsBuilder()
            .UseFunctionInvocation()
            .Build()
            .AsChatCompletionService();

    // Register chat completion
    var builder = Kernel.CreateBuilder();
    builder.Services.AddKeyedSingleton("bedrock", chatService);
    // Add plugins
    builder.Plugins.AddFromType<MenuPlugin>();
    // ...
    var kernel = builder.Build();
    ```

2.  **Use automatically tool calls**
    ```csharp
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
            { "max_tokens_to_sample", 1024 } // Required for Anthropic
        }
    };

    var messageContent = await chatCompletionService
        .GetChatMessageContentAsync(chatHistory,  promptExecutionSettings, kernel);
    Console.WriteLine(messageContent.Content);
    ```

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-04-02/ConsoleApp)