using Amazon.Runtime;

namespace Agents.Api;

internal sealed class BedrockHttpClientFactory(IHttpClientFactory httpClientFactory, string name) : HttpClientFactory
{
    public override HttpClient CreateHttpClient(IClientConfig clientConfig)
    {
        return httpClientFactory.CreateClient(name);
    }

    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig)
    {
        return true;
    }
}