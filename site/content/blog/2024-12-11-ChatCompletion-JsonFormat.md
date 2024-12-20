---
title: OpenAI chat completion with Json output format
description: Auzre/OpenAI chat completion supports json format with schema.
date: 2024-12-19
tags: [ ".NET", "AI", "Semantic Kernel" ]
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

I can't recall how many times I've tried to convince a LLM to return JSON so that I could perform API calls based on natural language inputs from users. Recently, I discovered that this functionality is natively supported by the [Semantic Kernel](https://github.com/microsoft/semantic-kernel) and Microsoft AI Extension Library. It is officially documented by the OpenAI API [here](https://platform.openai.com/docs/guides/structured-outputs). Note that this feature is only available in the latest large language models from GPT-4o/o1 and later. If you are using Azure OpenAI, ensure you have the supported versions when deploying models.

## Chat completion
[Semantic Kernel](https://github.com/microsoft/semantic-kernel) supports JSON output formatting in the ResponseFormat property from PromptExecutionSettings, as shown in the code below:

```csharp
// Configure Azure/OpenAI and semantic kernel first.

var chatCompletionService = kernel.Services.GetRequiredService<IChatCompletionService>();

var history = new ChatHistory();
history.AddSystemMessage("Extract the event information.");
history.AddUserMessage("Alice and Bob are going to a science fair on Friday.");

var jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
};
var responseFormat = CalendarEvent.JsonResponseSchema(jsonSerializerOptions);
        
var response = await chatCompletionService.GetChatMessageContentAsync(
    history, 
    new AzureOpenAIPromptExecutionSettings
    {
        ResponseFormat = responseFormat // Json schema
    });
// Json result    
var result = JsonSerializer.Deserialize<CalendarEvent>(response.ToString(), jsonSerializerOptions);

```

## Generate Json schema from types
JSON schema can be automatically generated using Microsoft.Extensions.AI.AIJsonUtilities, which is referenced from [Semantic Kernel](https://github.com/microsoft/semantic-kernel).

```csharp
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

        // Json schema from types with descriptions on properties
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
```

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-12-19/ConsoleApp)

