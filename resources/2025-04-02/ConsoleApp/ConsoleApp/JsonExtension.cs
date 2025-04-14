using System.Text;
using System.Text.Json;
using Amazon.Runtime.Documents;
using Amazon.Runtime.Documents.Internal.Transform;
using Microsoft.Extensions.AI;

namespace ConsoleApp;

internal sealed class ToolUseContent
{
    public required string CallId { get; init; }
    public required string Name { get; init; }
    public StringBuilder? Arguments { get; set; }
}

internal static class JsonExtension
{
    public static Document ToDocument(this JsonElement jsonElement, Func<string, bool> filter)
    {
        var values = new Dictionary<string, Document>();
        foreach (var jsonProperty in jsonElement.EnumerateObject().Where(x => filter(x.Name)))
        {
            var value = Document.FromObject(jsonProperty.Value);
            values.Add(jsonProperty.Name, value);
        }
        
        return new Document(values);
    }
    
    public static Document ToDocument(this IDictionary<string, object?>? dictionary, JsonSerializerOptions? options = null)
    {
        return dictionary is not null
            ? Document.FromObject(JsonSerializer.SerializeToNode(dictionary, options ?? AIJsonUtilities.DefaultOptions))
            : default;
    }

    public static string SerializeResult(this FunctionResultContent resultContent, JsonSerializerOptions? options = null)
    {
        var result = resultContent.Result as string;
        if (result is null && resultContent.Result is not null)
        {
            try
            {
                result = JsonSerializer.Serialize(resultContent.Result, options ?? AIJsonUtilities.DefaultOptions);
            }
            catch (NotSupportedException)
            {
                // skip
            }
        }

        return result ?? string.Empty;
    }

    public static Dictionary<string, object?> DeserializeArguments(this ToolUseContent toolUseContent, JsonSerializerOptions? options = null)
    {
        var json = toolUseContent.Arguments?.ToString();
        Dictionary<string, object?>? result = default;
        if (!string.IsNullOrEmpty(json))
        {
            result = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, options ?? AIJsonUtilities.DefaultOptions);
        }
        
        return result ?? new ();
    }
    
    public static Dictionary<string, object?>? DeserializeToDictionary(this Document document, JsonSerializerOptions? options = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            DocumentMarshaller.Instance.Write(writer, document);
        }
        if (stream.Length > 0)
        {
            stream.Seek(0, SeekOrigin.Begin);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(stream, options ?? AIJsonUtilities.DefaultOptions);
        }

        return default;
    }
}