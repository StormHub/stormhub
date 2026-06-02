using AgentSkillsDemo.skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace AgentSkillsDemo.Options;

internal static class DependencyInjection
{
    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration
            .GetSection(nameof(AgentChatOptions))
            .Get<AgentChatOptions>()
            ?? throw new InvalidOperationException($"{nameof(AgentChatOptions)} configuration required.");

        services.AddTransient<TraceHttpHandler>();
        services.AddHttpClient(options.Model)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(options.BaseUrl);
            })
            .AddHttpMessageHandler<TraceHttpHandler>();
        services.AddChatClient(options);

        services.AddTransient<AgentSkillsProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var skillsProvider = new AgentSkillsProvider(Path.Combine(AppContext.BaseDirectory, "skills"), 
                loggerFactory: loggerFactory);
            return skillsProvider;
        });

        services.AddTransient<AIAgent>(provider =>
        {
            var chatClient = provider.GetRequiredKeyedService<IChatClient>(options.Model);
            var skillsProvider = provider.GetRequiredService<AgentSkillsProvider>();
            
            var agentOptions = new ChatClientAgentOptions
            {
                Name = "UnitConverterAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = "You are a helpful assistant that can convert units. Use the available tools to load skills, read references, and perform conversions.",
                    Tools = [AIFunctionFactory.Create(Tool.Convert)]
                },
                AIContextProviders = [skillsProvider],
            };
            
            return chatClient.AsAIAgent(agentOptions);
        });
        return services;
    }

    private static IServiceCollection AddChatClient(this IServiceCollection services, AgentChatOptions options)
    {
        services.AddKeyedTransient<IChatClient>(options.Model,
            (provider, _) =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(options.Model);
                var ollamaApiClient = new OllamaApiClient(httpClient, options.Model);
                return ollamaApiClient;            
            });
        return services;
    }
}