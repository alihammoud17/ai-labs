using System.Net.Http.Headers;

namespace AiLab.Infrastructure.Mcp;

// Puts the MCP token on every request of the MCP HttpClient: initialize, tools/list and each tools/call.
// The MCP spec wants the header on each HTTP request, and the SaaS server is stateless, so it checks the token every time.
// The provider decides when a token is refreshed; this handler only attaches it, so a long chat keeps working
// after the 15-minute MCP token expires (a fixed HttpClientTransportOptions.AdditionalHeaders value couldn't).
// Transient, as AddHttpMessageHandler<T> expects; the provider it uses is a singleton, so the cache is shared.
public class BearerTokenHandler(SaasApiTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token = await tokenProvider.GetMcpTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
