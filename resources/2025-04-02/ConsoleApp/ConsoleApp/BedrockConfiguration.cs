using Amazon;

namespace ConsoleApp;

public sealed class BedrockConfiguration
{
    public required string Region { get; init; }
    
    public required string KeyId { get; init; }
    
    public required string AccessKey { get; init; }
    
    public required string Token { get; init; }
    
    public required string ModelId { get; init; }

    public RegionEndpoint RequireRegionEndpoint() =>
        RegionEndpoint.EnumerableAllRegions
            .FirstOrDefault(x => x.SystemName == Region)
        ?? throw new InvalidOperationException($"Unknown AWS Region: {Region}");
}