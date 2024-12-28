namespace ConsoleApp;

public sealed class AzureOpenAIConfig
{
    public required string Endpoint { get; init; }

    public string? APIKey { get; init; }
}