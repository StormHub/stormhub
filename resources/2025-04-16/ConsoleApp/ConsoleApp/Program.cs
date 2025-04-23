using ConsoleApp;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Plugins.Core;
using ModelContextProtocol.Server;

WebApplication? host = default;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddTransient<TraceHttpHandler>();
    builder.Services.AddHttpClient(nameof(OllamaChatClient))
        .AddHttpMessageHandler<TraceHttpHandler>();

    builder.Services.AddKeyedTransient<IChatClient>("local", (provider, _) =>
    {
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(nameof(OllamaChatClient));

        return new OllamaChatClient(new Uri("http://localhost:11434"), modelId: "phi4", httpClient);
    });

    // Kernel
    builder.Services.AddSingleton(provider =>
    {
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var chatClient = provider.GetRequiredKeyedService<IChatClient>("local");
        
        var kernelBuilder = Kernel.CreateBuilder();

        var chatCompletionService = chatClient.AsChatCompletionService();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        kernelBuilder.Services.AddSingleton(loggerFactory);
        kernelBuilder.Plugins.AddFromType<TimePlugin>("time");
        
        return kernelBuilder.Build();
    });

    builder.Services.AddKeyedSingleton<TemplateServerPrompt>("story", (provider, _) =>
    {
        const string json = """
        {
          "name" : "Story",
          "template": "Tell a story about {{$topic}} that is {{$length}} sentences long.",
          "description": "Generate a story about a topic.",
          "input_variables": [
            {
                "name": "topic",
                "description": "The topic of the story.",
                "is_required": true
            },
            {
               "name": "length",
               "description": "The number of sentences in the story.",
               "is_required": true
            }
          ],
          "output_variable": {
             "description": "The generated story."
          }
        }
        """;

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var config = PromptTemplateConfig.FromJson(json);
        return new TemplateServerPrompt(config, promptTemplateFactory: default, loggerFactory);
    });
    
    builder.Services.AddKeyedSingleton("humor", (provider, _) =>
    {
        const string json = """
        {
          "name" : "Humor",
          "template": "Is the following funny? \n'{{$input}}'\n Answer with yes, no or unsure in one word.",
          "description": "Determine whether input is funny or not.",
          "input_variables": [
            {
                "name": "input",
                "description": "The input text.",
                "is_required": true
            }
          ],
          "output_variable": {
             "description": "yes, no or unsure in one word."
          }
        }
        """;

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var config = PromptTemplateConfig.FromJson(json);
        return new TemplateAIFunction(config, promptTemplateFactory: default, loggerFactory);
    });

    builder.Services.AddKeyedSingleton("day", (provider, _) =>
    {
        const string text = """
            Today is: {{time.Date}}

            What day of the week it is after {{$length}} days?
            """;

        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var config = new PromptTemplateConfig(text)
        {
            Name = "day",
            Description = "Get day of week after number of days from today.",
        };
        return new TemplateAIFunction(config, promptTemplateFactory: default, loggerFactory);
    });

    var serverBuilder = builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithPrompts<StringFormatPrompt>();
    serverBuilder.Services.AddSingleton<McpServerPrompt>(provider => 
        provider.GetRequiredKeyedService<TemplateServerPrompt>("story"));
    serverBuilder.Services.AddSingleton<McpServerPrompt>(provider => 
        McpServerPrompt.Create(provider.GetRequiredKeyedService<TemplateAIFunction>("humor")));
    serverBuilder.Services.AddSingleton<McpServerPrompt>(provider => 
        McpServerPrompt.Create(provider.GetRequiredKeyedService<TemplateAIFunction>("day")));

    host = builder.Build();

    host.MapMcp();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Host terminated unexpectedly! \n{ex}");
}
finally
{
    if (host is not null)
    {
        await host.DisposeAsync();
    }
}