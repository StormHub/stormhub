using AgentSkillsDemo.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddAgent(context.Configuration);
    })
    .UseConsoleLifetime()
    .Build();

try
{
    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    
    await using var scope = host.Services.CreateAsyncScope();
    var agent = scope.ServiceProvider.GetRequiredService<AIAgent>();
    
    var question = "How many kilometers is a marathon (26.2 miles)? And how many pounds is 75 kilograms?";
    var response = await agent.RunAsync(question);
    Console.WriteLine($"Agent: {response.Text}");
    
    lifetime.StopApplication();
    await host.WaitForShutdownAsync(lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    Console.WriteLine($"Host terminated unexpectedly! \n{ex}");
}
finally
{
    host.Dispose();
}