namespace ConsoleApp;

public sealed class AzureAIServiceConfig
{
    public required Uri Endpoint { get; init; }

    public string? APIKey { get; init; }
}