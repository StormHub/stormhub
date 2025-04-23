---
title: Model context protocol server prompts with microsoft semantic kernel
description: Model context protocol (MCP) server prompts implementation with Semantic Kerneal.
date: 2025-04-16
tags: [ ".NET", "AI", "Semantic Kernel", 'MCP' ]
draft: true
---

# {{title}}

*{{date | readableDate }}*

The [Model Context Protocol (MCP)](https://modelcontextprotocol.io/introduction) aims to standardize connections between AI systems and data sources. This post demonstrates server prompt implemented with [Semantic Kernel](https://github.com/microsoft/semantic-kernel) and [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk). MCP server prompts are reusable templates [more details](https://modelcontextprotocol.io/docs/concepts/prompts).

## MCP Server prompts
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) defines a set of attributes for prompts
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
                new (ChatRole.User,content)
            ];
        }
    }    

    // Register for the prompt
    var serverBuilder = builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithPrompts<StringFormatPrompt>();
    
    ```
As the above show, MCP server prompts can be implemented easily without [Semantic Kernel](https://github.com/microsoft/semantic-kernel)

## Prompt tempates in Semantic Kernel
[Semantic Kernel](https://github.com/microsoft/semantic-kernel) supports prompt template format of default format (json/yaml), handlebars and liquid with plugins.  We can expose those as MCP prompts. 

1.  **Template configuration and factory:**
Work with PromptTemplateConfig and IPromptTemplateFactory in [Semantic Kernel](https://github.com/microsoft/semantic-kernel) for templates.
    ```csharp
    var templateConfig = new PromptTemplateConfig("Tell a joke about {{$topic}}.");
    IPromptTemplateFactory templateFactory = new KernelPromptTemplateFactory();
    var template = templateFactory.Create(templateConfig);
    // template.RenderAsync(...)
    ```

2.  **Expose prompts as McpServerPrompt :**
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

3.  **Expose prompts as AIFunction :**
Because McpServerPrompt also can be created from Microsoft.Extensions.AI.AIFunction, we can also implement it and exposed it to MCP server.
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
        provider.GetRequiredService<TemplateServerPrompt>());

    ```

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2025-04-16/ConsoleApp)