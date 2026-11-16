using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace App.Extensions;

internal sealed class AIAgentBuilder(IChatClient chatClient, IHttpClientFactory httpClientFactory, Uri endpoint, ILoggerFactory? loggerFactory)
{
    private readonly Uri _endpoint = endpoint;

    // One HttpClient for everything sent to the Api: the MCP transport and the endpoints the skills describe.
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(McpClient));

    private async Task<AgentSkillsProvider> CreateClient(CancellationToken cancellationToken = default)
    {
        var mcpClient = await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Name = "api",
                    Endpoint = _endpoint,
                },
                _httpClient,
                loggerFactory),
            loggerFactory: loggerFactory,
            cancellationToken: cancellationToken);

        var skillsProvider = new AgentSkillsProviderBuilder()
            .UseMcpSkills(mcpClient)
            .DisableCaching() // let the MCP source honor the server's TTLs
            // load_skill and read_skill_resource require approval by default. The run then ends with an approval
            // interrupt that the Client does not handle, so the skill is never loaded. Both tools only read skill
            // text from our own Api, so approval is turned off for them.
            .UseOptions(options =>
            {
                options.DisableLoadSkillApproval = true;
                options.DisableReadSkillResourceApproval = true;
            })
            .Build();

        return skillsProvider;
    }


    public async Task<AIAgent> Build(CancellationToken token = default)
    {
        var provider = await CreateClient(token);

        // Register the skills provider on the agent, not on the chat client. The agent wraps the chat client with
        // its own FunctionInvokingChatClient, so tools added by a provider below that layer (load_skill,
        // read_skill_resource) are never seen by it and fail with 'Requested function "load_skill" not found'.
        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions {
                Instructions = "You are a helpful shopping assistant.",
                Tools =
                [
                    AIFunctionFactory.Create(
                        SendAsync,
                        name: "http_request",
                        description: "Send an HTTP request to an endpoint described by a skill, on the same host that serves the skills. " +
                                     "Pass the method (GET, POST, PUT, PATCH, DELETE), the path and query only (for example " +
                                     "/catalog?q=laptop or /pricing/p_laptop_dell), and a JSON body when the endpoint takes one. " +
                                     "Returns the status code and response body."),
                ],
            },
            AIContextProviders = [provider],
        });
    }

    /// <summary>Sends a request through the shared HttpClient. Only relative paths on the Api host are accepted.</summary>
    private async Task<string> SendAsync(string method, string path, string? body = null, CancellationToken cancellationToken = default)
    {
        if (!path.StartsWith('/') || path.StartsWith("//"))
        {
            return "Error: path must be relative to the skills host and start with '/', for example /catalog?q=laptop.";
        }

        // The client's BaseAddress is .../mcp; a path starting with '/' resolves against the host root.
        using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), path);
        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return $"HTTP {(int)response.StatusCode}\n{content}";
    }
}
