---
title: Model context protocol server prompts with microsoft semantic kernel
description: Model context protocol (MCP) server prompts implementation with Semantic Kerneal.
date: 2025-04-16
tags: [ ".NET", "AI", "Semantic Kernel", 'MCP' ]
---

# {{title}}

*{{date | readableDate }}*

This post focuses on implementing server prompts, a key feature of the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/introduction) designed for reusable template definitions. We will explore how to implement these server prompts using both the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) and [Semantic Kernel](https://github.com/microsoft/semantic-kernel) for enhanced templating capabilities. Further details on MCP server prompts can be found in the [MCP documentation](https://modelcontextprotocol.io/docs/concepts/prompts).


## MCP Server Prompts via MCP C# SDK Attributes
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) allows for defining prompts through attributes. This method offers a direct implementation without requiring Semantic Kernel for basic string manipulation as the following example shows.

    ```csharp
    [McpServerPromptType]
    internal sealed class StringFormatPrompt
    {
        private readonly string _format;
        private readonly ILogger _logger;
        
        public StringFormatPrompt(ILogger<StringFormatPrompt> logger)
        {
            _logger = logger;
            _format = "Tell a joke about {0}.";
        }
        
        [McpServerPrompt(Name = "Joke"), Description("Tell a joke about a topic.")]
        public IReadOnlyCollection<ChatMessage> Format([Description("The topic of the joke.")] string topic)
        {
            _logger.LogInformation("Generating prompt with topic: {Topic}", topic);
            var content = string.Format(CultureInfo.InvariantCulture, _format, topic);
            
            return [
                new (ChatRole.User, content)
            ];
        }
    }    

    // Register for the prompt
    var serverBuilder = builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithPrompts<StringFormatPrompt>();
    ```

## Semantic Kernel Templates as MCP Server Prompts
Semantic Kernel provides templating capabilities through JSON/YAML, Handlebars, and Liquid formats, along with plugin support. These templates can be exposed as MCP prompts using the MCP C# SDK.

1.  **Prompt Templates in Semantic Kernel**
    Semantic Kernel templates are configured with PromptTemplateConfig, created by IPromptTemplateFactory implementations, and can be easily rendered with input variables for dynamic prompt generation.
    
    ```csharp
    var templateConfig = new PromptTemplateConfig("Tell a joke about {{$topic}}.");
    IPromptTemplateFactory templateFactory = new KernelPromptTemplateFactory();
    var template = templateFactory.Create(templateConfig);
    var text = await template.RenderAsync(kernel,
        new KernelArguments
        {
            { "topic", "cats" }
        });
    ```

2.  **Expose prompts as McpServerPrompt**
    McpServerPrompt is the abstract base class that represents an MCP prompt we can implement.

    ```csharp
    internal sealed class TemplateServerPrompt : McpServerPrompt
    {
        public TemplateServerPrompt(PromptTemplateConfig promptTemplateConfig, IPromptTemplateFactory? promptTemplateFactory, ILoggerFactory? loggerFactory)
        {
            promptTemplateFactory ??= new KernelPromptTemplateFactory(loggerFactory ?? NullLoggerFactory.Instance);
            _template = promptTemplateFactory.Create(promptTemplateConfig);
            
            // MCP prompt
            ProtocolPrompt = new()
            {
                Name = promptTemplateConfig.Name ?? _template.GetType().Name,
                Description = promptTemplateConfig.Description,
                Arguments = promptTemplateConfig.InputVariables
                    .Select(inputVariable =>
                        new PromptArgument
                        {
                            Name = inputVariable.Name,
                            Description = inputVariable.Description,
                            Required = inputVariable.IsRequired
                        })
                    .ToList(),
            };
        }
        
        public override async ValueTask<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
        {
            KernelArguments? arguments = default;
            
            var dictionary = request.Params?.Arguments;
            if (dictionary is not null)
            {
                arguments = new ();
                foreach (var (key, value) in dictionary)
                {
                    arguments[key] = value;
                }
            }

            var kernel = request.Services?.GetService<Kernel>() ?? new Kernel();
            var text = await _template.RenderAsync(kernel, arguments, cancellationToken);
            
            return 
                new GetPromptResult
                {
                    Messages = [
                        new PromptMessage
                        {
                            Content = new Content { Text = text }
                        } 
                ]
            };
        }
    }

    // Register for the prompt with DI and MCP server
    // builder.Services.AddSingleton<TemplateAIFunction>(...)
    var serverBuilder = builder.Services.AddMcpServer()
        .WithHttpTransport();
    serverBuilder.Services.AddSingleton<McpServerPrompt>(provider => 
        provider.GetRequiredService<TemplateServerPrompt>());
    ```

3.  **Exposing AIFunction as McpServerPrompt**
    The McpServerPrompt class provides a Create method to expose a Microsoft.Extensions.AI.AIFunction as an MCP server prompt.

    ```csharp
    internal sealed class TemplateAIFunction : AIFunction 
    {
        //...

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            KernelArguments kernelArguments = [];
            
            foreach (var argument in arguments)
            {
                kernelArguments[argument.Key] = argument.Value;
            }

            var kernel = arguments.Services?.GetService<Kernel>() ?? new Kernel();
            var text = await _template.RenderAsync(kernel, kernelArguments, cancellationToken);
            return text;
        }
    }

    // Register for the prompt with DI and MCP server
    // builder.Services.AddSingleton<TemplateAIFunction>(...)
    var serverBuilder = builder.Services.AddMcpServer()
        .WithHttpTransport();
    serverBuilder.Services.AddSingleton<McpServerPrompt>(provider => 
        McpServerPrompt.Create(provider.GetRequiredService<TemplateServerPrompt>()));

    ```

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-04-16/ConsoleApp)