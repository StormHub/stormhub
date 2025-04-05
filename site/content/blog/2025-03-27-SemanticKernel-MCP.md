---
title: Model context protocol integration with microsoft semantic kernel
description: Model context protocol (MCP) enables Semantic Kerneal to seamlessly connect with various data sources and tools.
date: 2025-03-27
tags: [ ".NET", "AI", "Semantic Kernel", 'MCP' ]
---

# {{title}}

*{{date | readableDate }}*
# Integrating Model Context Protocol with Semantic Kernel and Ollama

The [Model Context Protocol (MCP)](https://modelcontextprotocol.io/introduction) aims to standardize connections between AI systems and data sources. This post demonstrates integrating [mcp-playwright](https://github.com/executeautomation/mcp-playwright) with [Semantic Kernel](https://github.com/microsoft/semantic-kernel) and [phi4-mini](https://ollama.com/library/phi4-mini) (via Ollama) for browser automation.

## Setting up the Playwright MCP Server

1.  **Install the MCP Playwright package:**

    ```bash
    npm install @playwright/mcp
    ```

2.  **Add a script to `package.json`:**

    ```json
    {
      "scripts": {
        "server": "npx @playwright/mcp --port 8931"
      }
    }
    ```

3.  **Start the server:**

    ```bash
    npm run server
    ```

    This will launch the Playwright MCP server, displaying the port and endpoints in the console.

## Running phi4-mini with Ollama for Function Calling

For reliable function calling, [phi4-mini:latest](https://ollama.com/library/phi4-mini) (as of March 27, 2025) requires a custom Modelfile.

1.  **Create a custom Modelfile:** (See [example](https://github.com/StormHub/stormhub/tree/main/resources/2025-03-27/Modelfile))

2.  **Create the model in Ollama:**

    ```bash
    ollama create phi4-mini:latest -f <path/to/Modelfile>
    ```

## Implementing the MCP Client in Semantic Kernel

1.  **Install the MCP client NuGet package:**

    ```bash
    dotnet add package ModelContextProtocol --prerelease
    ```

2.  **Connect to the Playwright MCP server and retrieve tools:**

    ```csharp
    var mcpClient = await McpClientFactory.CreateAsync(
        new McpServerConfig
        {
            Id = "playwright",
            Name = "Playwright",
            TransportType = TransportTypes.Sse,
            Location = "http://localhost:8931"
        });
    var tools = await mcpClient.ListToolsAsync();
    ```

3.  **Configure Semantic Kernel with the MCP tools:**

    ```csharp
    var kernelBuilder = Kernel.CreateBuilder();
    kernelBuilder.AddOllamaChatCompletion(modelId: "phi4-mini");
    kernelBuilder.Plugins.AddFromFunctions(
        pluginName: "playwright",
        functions: tools.Select(x => x.AsKernelFunction()));
    var kernel = kernelBuilder.Build();

    var executionSettings = new PromptExecutionSettings
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
            options: new()
            {
                RetainArgumentTypes = true
            }),
        ExtensionData = new Dictionary<string, object>
        {
            { "temperature", 0 }
        }
    };

    var result = await kernel.InvokePromptAsync(
        "open browser and navigate to [https://www.google.com](https://www.google.com)",
        new KernelArguments(executionSettings));
    ```

This code snippet connects to the MCP server, retrieves available tools, and integrates them into Semantic Kernel as functions. The prompt instructs the model to open a browser and navigate to Google, demonstrating the integration.

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-03-27/ConsoleApp)