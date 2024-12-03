using System.Text;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.DataFormats;

namespace ConsoleApp;

internal sealed class AzureImageToText : IOcrEngine
{
    private readonly ImageAnalysisClient _imageAnalysisClient;
    private readonly ILogger _logger;
    
    public AzureImageToText(
        ImageAnalysisClient imageAnalysisClient, 
        ILogger<AzureImageToText> logger)
    {
        _imageAnalysisClient = imageAnalysisClient;
        _logger = logger;
    }

    public async Task<string> ExtractTextFromImageAsync(Stream imageContent, CancellationToken cancellationToken = default)
    {
        var imageData = await BinaryData.FromStreamAsync(imageContent, cancellationToken);
        
        var result =  await _imageAnalysisClient.AnalyzeAsync(imageData,
             VisualFeatures.Read,
             new ImageAnalysisOptions { GenderNeutralCaption = true },
             cancellationToken);

        _logger.LogDebug("{Response}", result.GetRawResponse().ToString());
        
        var buffer = new StringBuilder();
        if (result.HasValue)
        {
            foreach (var block in result.Value.Read.Blocks)
            {
                buffer.AppendLine();
                foreach (var line in block.Lines)
                {
                    buffer.AppendLine(line.Text);
                }
            }
        }
        
        return buffer.ToString();
    }
}