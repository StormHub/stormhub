using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

using ChatResponseFormat = OpenAI.Chat.ChatResponseFormat;

namespace ConsoleApp;

public sealed class CalendarEvent
{
    [Description("Name of the event")]
    public required string Name { get; init; }
    
    [Description("Day of the event")]
    public required string Day { get; init; }
    
    [Description("List of participants of the event")]
    public required string[] Participants { get; init; }

    public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
    {
        var inferenceOptions = new AIJsonSchemaCreateOptions
        {
            IncludeSchemaKeyword = false,
            DisallowAdditionalProperties = true,
        };

        var jsonElement = AIJsonUtilities.CreateJsonSchema(
            typeof(CalendarEvent),
            description: "Calendar event result",
            serializerOptions: jsonSerializerOptions,
            inferenceOptions: inferenceOptions);
        
        var kernelJsonSchema = KernelJsonSchema.Parse(jsonElement.GetRawText());
        var jsonSchemaData = BinaryData.FromObjectAsJson(kernelJsonSchema, jsonSerializerOptions);

        return ChatResponseFormat.CreateJsonSchemaFormat(
            nameof(CalendarEvent).ToLowerInvariant(),
            jsonSchemaData,
            jsonSchemaIsStrict: true);
    }
}