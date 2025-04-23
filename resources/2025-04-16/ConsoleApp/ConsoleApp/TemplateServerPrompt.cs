using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace ConsoleApp;

internal sealed class TemplateServerPrompt : McpServerPrompt
{
    private readonly IPromptTemplate _template;

    public TemplateServerPrompt(PromptTemplateConfig promptTemplateConfig, IPromptTemplateFactory? promptTemplateFactory, ILoggerFactory? loggerFactory)
    {
        promptTemplateFactory ??= new KernelPromptTemplateFactory(loggerFactory ?? NullLoggerFactory.Instance);
        _template = promptTemplateFactory.Create(promptTemplateConfig);
        
        ProtocolPrompt = new()
        {
            Name = promptTemplateConfig.Name ?? _template.GetType().Name,
            Description = promptTemplateConfig.Description,
            Arguments = promptTemplateConfig.InputVariables
                .Select(inputVariable =>
                    new PromptArgument
                    {
                        Name = inputVariable.Name,
                        Description = inputVariable.Description,
                        Required = inputVariable.IsRequired
                    })
                .ToList(),
        };
    }
    
    public override async ValueTask<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default)
    {
        KernelArguments? arguments = default;
        
        var dictionary = request.Params?.Arguments;
        if (dictionary is not null)
        {
            arguments = new ();
            foreach (var (key, value) in dictionary)
            {
                arguments[key] = value;
            }
        }

        var kernel = request.Services?.GetService<Kernel>() ?? new Kernel();
        var text = await _template.RenderAsync(kernel, arguments, cancellationToken);
        
        return new GetPromptResult
            {
                Messages = [
                    new PromptMessage
                    {
                        Content = new Content { Text = text }
                    } 
            ]
        };
    }

    public override Prompt ProtocolPrompt { get; }
}