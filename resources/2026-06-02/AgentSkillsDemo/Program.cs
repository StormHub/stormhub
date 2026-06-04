using AgentSkillsDemo;
using AgentSkillsDemo.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost? host = default;
try
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            services.AddAgent(context.Configuration);
        })
        .UseConsoleLifetime()
        .Build();

    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    
    await using var scope = host.Services.CreateAsyncScope();
    var agent = scope.ServiceProvider.GetRequiredService<AIAgent>();

    var session = await agent.CreateSessionAsync(lifetime.ApplicationStopping);

    // Turn 1 — triggers the skill and loads its content into the session.
    var turn1 = "How many kilometers is a marathon (26.2 miles)? And how many pounds is 75 kilograms?";
    var response1 = await agent.RunAsync(turn1, session);
    Console.WriteLine($"\n[Turn 1] {response1.Text}\n");

    // Turn 2 — compaction runs before this invocation, summarizing turn 1's history
    // (including the loaded skill content) instead of forwarding it verbatim.
    var turn2 = "And how many kilometers is 10 miles?";
    var response2 = await agent.RunAsync(turn2, session);
    Console.WriteLine($"\n[Turn 2] {response2.Text}\n");

    if (session.TryGetInMemoryChatHistory(out var history))
    {
        History.Dump(history, "Session history after both turns");
    }
    else
    {
        Console.WriteLine("No in-memory chat history available for this session.");
    }

    lifetime.StopApplication();
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