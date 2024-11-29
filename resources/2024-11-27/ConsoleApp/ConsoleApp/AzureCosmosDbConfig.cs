using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace ConsoleApp;

public sealed class AzureCosmosDbConfig
{
    [Required] public required string Endpoint { get; init; }
    
    public string? APIKey { get; init; }

    internal static readonly JsonSerializerOptions DefaultJsonSerializerOptions;

    private static readonly VectorEmbeddingPolicy VectorEmbeddingPolicy = new(
    [
        new Embedding
        {
            DataType = VectorDataType.Float32,
            Dimensions = 1536,
            DistanceFunction = DistanceFunction.Cosine,
            Path = $"/{AzureCosmosDbMemoryRecord.VectorField}",
        }
    ]);

    private static readonly IndexingPolicy IndexingPolicy = new()
    {
        VectorIndexes =
        [
            new()
            {
                Path = $"/{AzureCosmosDbMemoryRecord.VectorField}",
                Type = VectorIndexType.QuantizedFlat,
            }
        ],
    };

    internal static ContainerProperties GetContainerProperties(string containerId)
    {
        var properties = new ContainerProperties(
            containerId,
            $"/{AzureCosmosDbMemoryRecord.FileField}")
        {
            VectorEmbeddingPolicy = VectorEmbeddingPolicy,
            IndexingPolicy = IndexingPolicy,
        };
        properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });    
        properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = $"/{AzureCosmosDbMemoryRecord.VectorField}/*" });

        return properties;
    }

    static AzureCosmosDbConfig()
    {
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        DefaultJsonSerializerOptions = jsonSerializerOptions;
    }
}
