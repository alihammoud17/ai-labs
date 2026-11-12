using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AiLab.Infrastructure.Mcp;

// One MCP client for the SaaS API's server, over Streamable HTTP. ConnectAsync sends initialize and tools/list
// and returns the tools as AITools: each McpClientTool is an AIFunction whose name, description and schema came from
// tools/list, and invoking it sends tools/call through the same client (so through BearerTokenHandler).
// The tools work only while the client is connected; after DisconnectAsync they must not be sent to the model any more.
public sealed class SaasMcpToolSource(
    IHttpClientFactory httpClientFactory,
    IOptions<SaasMcpSettings> options,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly SaasMcpSettings _settings = options.Value;
    private readonly ILogger _logger = loggerFactory.CreateLogger<SaasMcpToolSource>();

    private McpClient? _client;
    private IList<AITool>? _tools;

    public bool IsConnected => _client is not null;

    // Connected already: the same tools, no second connection
    public async Task<IList<AITool>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null && _tools is not null)
            return _tools;

        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out Uri? endpoint))
            throw new InvalidOperationException("SaasMcp:Endpoint is missing or not an absolute URL in appsettings.json.");

        // From the factory, so it has BearerTokenHandler; the factory owns its handlers, so the transport must not dispose it
        HttpClient httpClient = httpClientFactory.CreateClient(SaasMcpServices.McpHttpClientName);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            // Explicit: the default AutoDetect can fall back to the old SSE transport
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "saas",
        }, httpClient, loggerFactory, ownsHttpClient: false);

        // initialize is the first request through BearerTokenHandler, so a bad login or a stopped API fails here
        McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            // Shown to the server in initialize
            ClientInfo = new Implementation { Name = "AiLab.Console", Version = "1.0.0" },
        }, loggerFactory, cancellationToken);

        try
        {
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            // IList<T> is invariant, so the McpClientTools go into a new list typed as AITool
            _tools = [.. tools];
            _client = client;
        }
        catch
        {
            // Listing failed after the connection opened: don't leave it open
            await client.DisposeAsync();
            throw;
        }

        _logger.LogInformation("Connected to the SaaS MCP server at {Endpoint}: {ToolCount} tools", endpoint, _tools.Count);
        return _tools;
    }

    public async Task DisconnectAsync()
    {
        if (_client is null)
            return;

        McpClient client = _client;
        _client = null;
        _tools = null;
        await client.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
