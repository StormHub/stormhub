using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace ConsoleApp;

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