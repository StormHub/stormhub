namespace AgentSkillsDemo.Options;

public sealed class AgentChatOptions
{
    public required string Model { get; init; }
    
    public required string BaseUrl { get; init; }
}