using Microsoft.Extensions.Logging;

namespace ConsoleApp;

internal sealed class TraceHttpHandler(ILogger<TraceHttpHandler> logger) : DelegatingHandler
{
    private readonly ILogger _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
#if DEBUG
        if (request.Content is not null)
        {
            await using var requestStream = await GetContentStream(request.Content);
            var requestStreamContent = new StreamContent(requestStream);
            var requestContent = await requestStreamContent.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("{Method} {RequestUri} {RequestContent}", request.Method, request.RequestUri, requestContent);
        }
#endif
        var response = await base.SendAsync(request, cancellationToken);
#if DEBUG
        
        _logger.LogInformation("Response headers");
        foreach (var header in response.Headers)
        {
            _logger.LogInformation("{Key}={Value}", header.Key, string.Join(";", header.Value));
        }

        _logger.LogInformation("Response content headers");
        foreach (var header in response.Content.Headers)
        {
            _logger.LogInformation("{Key}={Value}", header.Key, string.Join(";", header.Value));
        }

        var responseStream = await GetContentStream(response.Content);
        var responseStreamContent = new StreamContent(responseStream);
        var responseContent = await responseStreamContent.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("{Status} {ResponseContent}", response.StatusCode, responseContent);

        // Rewind the stream position for downstream reads
        responseStream.Position = 0;
        response.Content = responseStreamContent;
#endif

        return response;
    }

    private static async Task<Stream> GetContentStream(HttpContent httpContent)
    {
        var stream = new MemoryStream();
        await httpContent.CopyToAsync(stream);
        stream.Position = 0;
        return stream;
    }
}