using Amazon;

namespace Agents.Api;

public sealed class BedrockConfiguration
{
    public required string Region { get; init; }

    public required string KeyId { get; init; }

    public required string AccessKey { get; init; }

    public required string Token { get; init; }

    public RegionEndpoint RequireRegionEndpoint()
    {
        return RegionEndpoint.EnumerableAllRegions
                   .FirstOrDefault(x => x.SystemName == Region)
               ?? throw new InvalidOperationException($"Unknown AWS Region: {Region}");
    }
}