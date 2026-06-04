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

    var question = "How many kilometers is a marathon (26.2 miles)? And how many pounds is 75 kilograms?";
    var session = await agent.CreateSessionAsync(lifetime.ApplicationStopping);
    var response = await agent.RunAsync(question, session);
    Console.WriteLine($"Agent: {response.Text}");

    if (session.TryGetInMemoryChatHistory(out var history))
    {
        History.Dump(history, "Session history after run");
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