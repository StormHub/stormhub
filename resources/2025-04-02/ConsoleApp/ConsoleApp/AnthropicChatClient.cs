using System.Runtime.CompilerServices;
using System.Text;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Endpoints;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using Microsoft.Extensions.AI;

namespace ConsoleApp;

internal sealed class AnthropicChatClient : IChatClient
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly ChatClientMetadata _metadata;

    public AnthropicChatClient(IAmazonBedrockRuntime bedrockRuntime, string defaultModelId)
    {
        _bedrockRuntime = bedrockRuntime;
        _metadata = new ChatClientMetadata(
            "anthropic",
            GetEndpointUri(bedrockRuntime),
            defaultModelId);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (system, messageList) = ToRequestMessages(messages);

        var request = new ConverseRequest
        {
            ModelId = options?.ModelId ?? _metadata.DefaultModelId,
            Messages = messageList,
            System = system,
            InferenceConfig = GetInferenceConfiguration(options),
            AdditionalModelRequestFields = GetTollChoice(options),
            AdditionalModelResponseFieldPaths = [],
            GuardrailConfig = null,
            ToolConfig = GetToolConfiguration(options)
        };

        var response = await _bedrockRuntime.ConverseAsync(request, cancellationToken);

        var chatMessage = new ChatMessage(
            ToChatRole(response.Output.Message.Role),
            new List<AIContent>(response.Output.Message.Content.SelectMany(ToAIContents)))
        {
            RawRepresentation = response.Output.Message
        };

        return new ChatResponse(chatMessage)
        {
            FinishReason = ToChatFinishReason(response.StopReason),
            ModelId = request.ModelId,
            Usage = ToUsageDetails(response.Usage),
            RawRepresentation = response
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (system, messageList) = ToRequestMessages(messages);
        var request = new ConverseStreamRequest
        {
            ModelId = options?.ModelId ?? _metadata.DefaultModelId,
            Messages = messageList,
            System = system,
            InferenceConfig = GetInferenceConfiguration(options),
            AdditionalModelRequestFields = GetTollChoice(options),
            AdditionalModelResponseFieldPaths = [],
            GuardrailConfig = null,
            ToolConfig = GetToolConfiguration(options)
        };

        var response = await _bedrockRuntime.ConverseStreamAsync(request, cancellationToken);

        ChatRole? role = default;
        ChatFinishReason? finishReason = default;
        AdditionalPropertiesDictionary? additionalProperties = default;
        Dictionary<int, ToolUseContent>? toolUseContents = default;

        await foreach (var streamEvent in response.Stream.WithCancellation(cancellationToken))
        {
            role ??= streamEvent is MessageStartEvent messageStartEvent ? ToChatRole(messageStartEvent.Role) : default;

            switch (streamEvent)
            {
                case ContentBlockStartEvent { ContentBlockIndex: not null } blockStartEvent:
                    {
                        toolUseContents ??= [];
                        toolUseContents[blockStartEvent.ContentBlockIndex.Value] =
                            new ToolUseContent
                            {
                                CallId = blockStartEvent.Start.ToolUse.ToolUseId,
                                Name = blockStartEvent.Start.ToolUse.Name
                            };
                    }
                    break;

                case ContentBlockDeltaEvent blockDeltaEvent:
                    {
                        if (blockDeltaEvent.Delta.Text is not null)
                            yield return new()
                            {
                                FinishReason = finishReason,
                                ModelId = request.ModelId,
                                Role = role,
                                RawRepresentation = streamEvent,
                                AdditionalProperties = additionalProperties,
                                Contents = [new TextContent(blockDeltaEvent.Delta.Text)]
                            };

                        if (blockDeltaEvent.Delta.ToolUse is not null
                            && blockDeltaEvent.ContentBlockIndex.HasValue
                            && toolUseContents is not null
                            && toolUseContents.TryGetValue(blockDeltaEvent.ContentBlockIndex.Value, out var toolUseContent))
                        {
                            var input = blockDeltaEvent.Delta.ToolUse.Input;
                            if (!string.IsNullOrEmpty(input))
                            {
                                toolUseContent.Arguments ??= new StringBuilder();
                                toolUseContent.Arguments.Append(input);
                            }
                        }
                    }
                    break;

                case ContentBlockStopEvent { ContentBlockIndex: not null } blockStopEvent
                    when toolUseContents is not null
                         && toolUseContents.TryGetValue(blockStopEvent.ContentBlockIndex.Value, out var toolUse):
                    {
                        yield return new()
                        {
                            FinishReason = finishReason,
                            ModelId = request.ModelId,
                            Role = role,
                            RawRepresentation = streamEvent,
                            AdditionalProperties = additionalProperties,
                            Contents =
                            [
                                new FunctionCallContent(
                                    toolUse.CallId,
                                    toolUse.Name,
                                    toolUse.DeserializeArguments())
                            ]
                        };
                    }
                    break;

                case MessageStopEvent messageStopEvent:
                    {
                        finishReason ??= ToChatFinishReason(messageStopEvent.StopReason);
                        if (additionalProperties is null)
                        {
                            var properties = messageStopEvent.AdditionalModelResponseFields.DeserializeToDictionary();
                            if (properties is not null)
                                additionalProperties = new AdditionalPropertiesDictionary(properties);
                        }
                    }
                    break;

                case ConverseStreamMetadataEvent metadataEvent:
                    {
                        var usage = ToUsageDetails(metadataEvent.Usage);
                        yield return new()
                        {
                            FinishReason = finishReason,
                            ModelId = request.ModelId,
                            Role = role,
                            RawRepresentation = streamEvent,
                            AdditionalProperties = additionalProperties,
                            Contents = usage is not null ? [new UsageContent(usage)] : []
                        };
                    }
                    break;
            }
        }
    }

    private static (List<SystemContentBlock>, List<Message>) ToRequestMessages(IEnumerable<ChatMessage> chatMessages)
    {
        var messages = new List<Message>();
        var system = new List<SystemContentBlock>();
        foreach (var chatMessage in chatMessages)
            if (chatMessage.Role == ChatRole.System)
            {
                system.Add(new SystemContentBlock { Text = chatMessage.Text });
            }
            else
            {
                var message = new Message
                {
                    Role = chatMessage.Role == ChatRole.Assistant
                        ? ConversationRole.Assistant
                        : ConversationRole.User,
                    Content = [..chatMessage.Contents.Select(FromAIContent)]
                };
                messages.Add(message);
            }

        return (system, messages);
    }

    private static IEnumerable<AIContent> ToAIContents(ContentBlock contentBlock)
    {
        if (contentBlock.Text is not null) yield return new TextContent(contentBlock.Text);

        if (contentBlock.ToolUse is not null)
            yield return new FunctionCallContent(
                contentBlock.ToolUse.ToolUseId,
                contentBlock.ToolUse.Name,
                contentBlock.ToolUse.Input.DeserializeToDictionary());
    }

    private static ContentBlock FromAIContent(AIContent content) =>
        content switch
        {
            TextContent textContent => new()
            {
                Text = textContent.Text
            },

            DataContent imageContent when imageContent.HasTopLevelMediaType("image") => new()
            {
                Image = new()
                {
                    Source = new()
                    {
                        Bytes = new MemoryStream(imageContent.Data.ToArray(), false)
                    },
                    Format = imageContent.MediaType
                }
            },

            DataContent documentContent when documentContent.HasTopLevelMediaType("application") => new()
            {
                Document = new()
                {
                    Source = new DocumentSource
                    {
                        Bytes = new MemoryStream(documentContent.Data.ToArray(), false)
                    },
                    Format = documentContent.MediaType
                }
            },

            FunctionCallContent functionCallContent => new()
            {
                ToolUse = new()
                {
                    ToolUseId = functionCallContent.CallId,
                    Name = functionCallContent.Name,
                    Input = functionCallContent.Arguments.ToDocument()
                }
            },

            FunctionResultContent functionResultContent => new()
            {
                ToolResult = new()
                {
                    ToolUseId = functionResultContent.CallId,
                    Status = functionResultContent.Exception is not null
                        ? ToolResultStatus.Error
                        : ToolResultStatus.Success,
                    Content =
                    [
                        new ToolResultContentBlock
                        {
                            Text = functionResultContent.SerializeResult()
                        }
                    ]
                }
            },

            _ => throw new NotSupportedException($"Unsupported content type: {content.GetType()}")
        };


    private static Document GetTollChoice(ChatOptions? options)
    {
        var values = new Dictionary<string, Document>();
        if (options is { Tools.Count: > 0, ToolMode: RequiredChatToolMode toolMode })
            values.Add("tool_choice",
                new Document
                {
                    { "type", toolMode.RequiredFunctionName is null ? "any" : "tool" },
                    { "name", toolMode.RequiredFunctionName }
                });

        return new Document(values);
    }

    private static ToolConfiguration? GetToolConfiguration(ChatOptions? options)
    {
        ToolConfiguration? toolConfig = default;
        if (options?.Tools is { Count: > 0 })
        {
            toolConfig = new ToolConfiguration
            {
                Tools = [..options.Tools.OfType<AIFunction>().Select(FromAIFunction)]
            };
        }

        return toolConfig;
    }

    private static Tool FromAIFunction(AIFunction function) =>
        new()
        {
            ToolSpec = new()
            {
                Name = function.Name,
                Description = function.Description,
                InputSchema = new ToolInputSchema
                {
                    Json = function.JsonSchema.ToDocument(propertyName =>
                        propertyName is "type" or "properties" or "required")
                }
            }
        };

    private static ChatFinishReason ToChatFinishReason(StopReason stopReason)
    {
        if (stopReason == StopReason.Max_tokens) return ChatFinishReason.Length;

        if (stopReason == StopReason.Tool_use) return ChatFinishReason.ToolCalls;

        if (stopReason == StopReason.Content_filtered) return ChatFinishReason.ContentFilter;

        if (stopReason == StopReason.Stop_sequence
            || stopReason == StopReason.End_turn)
            return ChatFinishReason.Stop;

        return new ChatFinishReason(stopReason.Value);
    }

    private static UsageDetails? ToUsageDetails(TokenUsage? usage) =>
        usage is not null
            ? new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.TotalTokens
            }
            : default;

    private static ChatRole ToChatRole(ConversationRole role) =>
        role == ConversationRole.Assistant
            ? ChatRole.Assistant
            : role == ConversationRole.User
                ? ChatRole.User
                : new ChatRole(role.Value);

    private static InferenceConfiguration GetInferenceConfiguration(ChatOptions? options)
    {
        var inferenceConfig = new InferenceConfiguration
        {
            Temperature = options?.Temperature,
            TopP = options?.TopP,
            MaxTokens = options?.MaxOutputTokens,
            StopSequences = new List<string>(options?.StopSequences ?? [])
        };
        
        if ((options?.AdditionalProperties?.TryGetValue("max_tokens_to_sample", out var value) ?? false)
            && value is int maxTokens)
            inferenceConfig.MaxTokens = maxTokens;
        
        inferenceConfig.MaxTokens ??= 1024; // Default value for Anthropic
        return inferenceConfig;
    }

    private static Uri? GetEndpointUri(IAmazonBedrockRuntime bedrockRuntime)
    {
        var config = bedrockRuntime.Config;
        var endpoint = config.EndpointProvider.ResolveEndpoint(
            new BedrockRuntimeEndpointParameters
            {
                Region = config.RegionEndpoint.SystemName,
                UseDualStack = config.UseDualstackEndpoint,
                UseFIPS = config.UseFIPSEndpoint
            });

        return endpoint != null ? new Uri(endpoint.URL) : default;
    }
    
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null :
        serviceType == typeof(ChatClientMetadata) ? _metadata :
        serviceType.IsInstanceOfType(this) ? this :
        null;

    public void Dispose() => _bedrockRuntime.Dispose();
}