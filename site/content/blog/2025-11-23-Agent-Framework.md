---
title: Building End-to-End Local AI Agents with Microsoft Agent Framework and AG-UI
description: The Agent–User Interaction (AG-UI) Protocol implemented by Microsoft Agent Framework.
date: 2025-11-23
tags: [ ".NET", "AI", "Agent", 'AG-UI' ]
---

# {{title}}

*{{date | readableDate }}*

The [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview) significantly elevates AI agent orchestration. A standout feature is its implementation of the **[Agent–User Interaction (AG-UI) Protocol](https://docs.ag-ui.com/introduction)**, which standardizes how AI agents connect to user-facing applications.

Below is a quick-start guide to connecting these components into a fully end-to-end solution using local **Ollama** models.

## 1\. Service Configuration

First, configure the dependency injection container. The `ChatClientAgent` is based on the `IChatClient` abstraction from `Microsoft.Extensions.AI`.

*Note: We register the agent as a Keyed Service to allow for multiple distinct agents within the same host.*

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register the Ollama Client
builder.Services.AddTransient<IChatClient>(provider =>
{
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    // Ensure you use a wrapper that handles standard formatting 
    // (see Implementation Note below)
    return new OllamaApiClient(factory.CreateClient("OllamaClient"), "phi4");
});

// 2. Register the AI Agent
builder.Services.AddKeyedTransient<ChatClientAgent>(
    "local-ollama-agent",
    (provider, key) =>
    {
        var options = new ChatClientAgentOptions
        {
            Id = key.ToString(),
            Name = "Local Assistant",
            Description = "An AI agent running on local Ollama.",
            ChatOptions = new ChatOptions { Temperature = 0 }
        };

        return provider.GetRequiredService<IChatClient>()
            .CreateAIAgent(options, provider.GetRequiredService<ILoggerFactory>());
    });
```

## 2\. Expose the AG-UI Endpoint

Once configured, map the agent instance directly to an HTTP route. This exposes the agent via the standard AG-UI protocol.

```csharp
var agent = app.Services.GetRequiredKeyedService<ChatClientAgent>("local-ollama-agent");

// Expose the agent on the root path
app.MapAGUI("/", agent);
```

## 3\. Connect a Client

To consume the agent programmatically, the framework provides the `AGUIChatClient`. This allows .NET applications to communicate with your agent over HTTP seamlessly.

```csharp
var chatClient = new AGUIChatClient(
    httpClient,
    "http://localhost:5000",
    provider.GetRequiredService<ILoggerFactory>());

var clientAgent = chatClient.CreateAIAgent(
    name: "local-client",
    description: "AG-UI Client Agent");
```

> **Frontend Integration:** The [AG-UI Protocol](https://docs.ag-ui.com/quickstart/applications) also offers ready-made libraries for TypeScript and Python, allowing you to spin up frontend interfaces in minutes.

## Implementation Note: Protocol Compliance

The AG-UI protocol mandates that all messages contain a `messageId` property. Native Ollama responses do not currently provide this. To ensure compatibility, I created a [simple wrapper class](https://github.com/StormHub/stormhub/blob/main/resources/2025-11-23/ConsoleApp/WebApi/OllamaChatClient.cs) to inject the required IDs into the Ollama response stream.

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-11-23/ConsoleApp)