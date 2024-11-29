using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.MemoryStorage;

using Embedding = Microsoft.KernelMemory.Embedding;

namespace ConsoleApp;

internal class AzureCosmosDbMemoryRecord
{
    internal const string VectorField = "embedding";
    internal const string FileField = "file";
    internal const string TagsField = "tags";

    private const string IdField = "id";
    private const string PayloadField = "payload";
    
    [JsonPropertyName(IdField)]
    public required string Id { get; init; }
    
    [JsonPropertyName(FileField)]
    public required string File { get; init; }

    [JsonPropertyName(PayloadField)] 
    public required Dictionary<string, object> Payload { get; init; } = [];

    [JsonPropertyName(TagsField)]
    public TagCollection Tags { get; init; } = [];
    
    [JsonPropertyName(VectorField)]
    [JsonConverter(typeof(Embedding.JsonConverter))]
    public Embedding Vector { get; init; }

    internal PartitionKey GetPartitionKey() => new(File);
    
    internal static string Columns(string? alias = default, bool withEmbeddings = false) => 
        string.Join(',', GetColumns(alias, withEmbeddings));

    private static IEnumerable<string> GetColumns(string? alias = default, bool withEmbeddings = false)
    {
        string[] fieldNames = [IdField, FileField, TagsField, VectorField, PayloadField];
        foreach (var name in fieldNames)
        {
            if (!withEmbeddings 
                && string.Equals(name, VectorField, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return string.IsNullOrEmpty(alias) ? name : $"{alias}.{name}";
        }
    }

    internal MemoryRecord ToMemoryRecord(bool withEmbedding = true)
    {
        var id = DecodeId(Id);
        var memoryRecord = new MemoryRecord
        {
            Id = id,
            Payload = Payload,
            Tags = Tags
        };

        if (withEmbedding)
        {
            memoryRecord.Vector = Vector;
        }
        
        return memoryRecord;
    }
    
    internal static AzureCosmosDbMemoryRecord FromMemoryRecord(MemoryRecord record)
    {
        var id = EncodeId(record.Id);
        var fileId = record.GetFileId();
        
        var memoryRecord = new AzureCosmosDbMemoryRecord
        {
            Id = id,
            File = fileId,
            Payload = record.Payload,
            Tags = record.Tags,
            Vector = record.Vector
        };
        
        return memoryRecord;
    }
    
    private static string EncodeId(string recordId)
    {
        var bytes = Encoding.UTF8.GetBytes(recordId);
        return Convert.ToBase64String(bytes).Replace('=', '_');
    }

    private static string DecodeId(string encodedId)
    {
        var bytes = Convert.FromBase64String(encodedId.Replace('_', '='));
        return Encoding.UTF8.GetString(bytes);
    }
}