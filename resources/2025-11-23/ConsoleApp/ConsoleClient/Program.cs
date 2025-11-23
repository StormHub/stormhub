using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.AGUI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost? host = default;

try
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((_, services) =>
        {
            services.AddHttpClient("local");

            services.AddTransient<AIAgent>(provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient("local");
                var jsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString,
                };
                jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

                var chatClient = new AGUIChatClient(
                    httpClient,
                    "http://localhost:5000",
                    provider.GetRequiredService<ILoggerFactory>(),
                    jsonSerializerOptions);

                var agent = chatClient.CreateAIAgent(
                    name: "local-client",
                    description: "AG-UI Client Agent");

                return agent;
            });
        })
        .UseConsoleLifetime()
        .Build();

    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await using var scope = host.Services.CreateAsyncScope();
    var agent = scope.ServiceProvider.GetRequiredService<AIAgent>();
    var thread = agent.GetNewThread();
    List<ChatMessage> messages = [];
    while (!lifetime.ApplicationStopping.IsCancellationRequested)
    {
        Console.Write("\nUser (:q or quit to exit): ");
        var message = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(message))
        {
            Console.WriteLine("Request cannot be empty.");
            continue;
        }

        if (message is ":q" or "quit")
        {
            lifetime.StopApplication();
            break;
        }

        messages.Add(new(ChatRole.User, message));

        // Call RunStreamingAsync to get streaming updates
        var isFirstUpdate = true;
        string? threadId = null;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in agent.
                           RunStreamingAsync(messages, thread, cancellationToken: lifetime.ApplicationStopping))
        {
            // Use AsChatResponseUpdate to access ChatResponseUpdate properties
            var chatUpdate = update.AsChatResponseUpdate();
            updates.Add(chatUpdate);
            if (chatUpdate.ConversationId != null) threadId = chatUpdate.ConversationId;

            // Display run started information from the first update
            if (isFirstUpdate && threadId != null && update.ResponseId != null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Run Started - Thread: {threadId}, Run: {update.ResponseId}]");
                Console.ResetColor();
                isFirstUpdate = false;
            }

            // Display different content types with appropriate formatting
            foreach (var content in update.Contents)
                switch (content)
                {
                    case TextContent textContent:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(textContent.Text);
                        Console.ResetColor();
                        break;

                    case FunctionCallContent functionCallContent:
                        Console.ForegroundColor = ConsoleColor.Green;
                        var arguments =
                            functionCallContent.Arguments?.Select(x => $"Name: {x.Key} \n Value: {x.Value}") ?? [];
                        Console.WriteLine(
                            $"\n[Function Call - Name: {functionCallContent.Name}, Arguments: {string.Join('\n', arguments)}]");
                        Console.ResetColor();
                        break;

                    case FunctionResultContent functionResultContent:
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine(functionResultContent.Exception != null
                            ? $"\n[Function Result - Exception: {functionResultContent.Exception}]"
                            : $"\n[Function Result - Result: {functionResultContent.Result}]");
                        Console.ResetColor();
                        break;

                    case ErrorContent errorContent:
                        Console.ForegroundColor = ConsoleColor.Red;
                        var code = errorContent.AdditionalProperties?["Code"] as string ?? "Unknown";
                        Console.WriteLine($"\n[Error - Code: {code}, Message: {errorContent.Message}]");
                        Console.ResetColor();
                        break;
                }
        }

        if (updates.Count > 0 && !updates[^1].Contents.Any(c => c is TextContent))
        {
            var lastUpdate = updates[^1];
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine($"[Run Ended - Thread: {threadId}, Run: {lastUpdate.ResponseId}]");
            Console.ResetColor();
        }

        messages.Clear();
        Console.WriteLine();
    }

    await host.WaitForShutdownAsync(lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    Console.WriteLine($"Host terminated unexpectedly! \n{ex}");
}
finally
{
    host?.Dispose();
}