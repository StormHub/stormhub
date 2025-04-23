using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;

namespace ConsoleApp;

internal sealed class AIFunctionSchema
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("properties")]
    public Dictionary<string, JsonElement> Properties { get; } = [];

    [JsonPropertyName("required"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }
}

internal sealed class TemplateAIFunction : AIFunction
{
    private readonly IPromptTemplate _template;

    public TemplateAIFunction(PromptTemplateConfig promptTemplateConfig, IPromptTemplateFactory? promptTemplateFactory, ILoggerFactory? loggerFactory)
    {
        promptTemplateFactory ??= new KernelPromptTemplateFactory(loggerFactory ?? NullLoggerFactory.Instance);
        _template = promptTemplateFactory.Create(promptTemplateConfig);

        var functionSchema = new AIFunctionSchema
        {
            Description = promptTemplateConfig.Description ?? string.Empty
        };
        
        foreach (var inputVariable in promptTemplateConfig.InputVariables)
        {
            var schema = AIJsonUtilities.CreateJsonSchema(
                type: default, 
                description: inputVariable.Description, 
                hasDefaultValue: inputVariable.Default is not null, 
                defaultValue: inputVariable.Default);
            functionSchema.Properties[inputVariable.Name] = schema;
            if (inputVariable.IsRequired)
            {
                functionSchema.Required ??= [];
                functionSchema.Required.Add(inputVariable.Name);
            }
        }
        
        JsonSchema = JsonSerializer.SerializeToElement(functionSchema, AIJsonUtilities.DefaultOptions);
        Name = promptTemplateConfig.Name ?? _template.GetType().Name;
        Description = functionSchema.Description;
    }

    public override string Name { get; }

    public override string Description { get; }
    
    public override JsonElement JsonSchema { get; }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        KernelArguments kernelArguments = [];
        
        foreach (var argument in arguments)
        {
            kernelArguments[argument.Key] = argument.Value;
        }

        var kernel = arguments.Services?.GetService<Kernel>() ?? new Kernel();
        var text = await _template.RenderAsync(kernel, kernelArguments, cancellationToken);
        return text;
    }
}