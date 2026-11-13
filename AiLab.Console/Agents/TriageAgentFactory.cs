using AiLab.Application.Agents;
using AiLab.Application.Services;
using AiLab.Domain.Constants;
using AiLab.Domain.Interfaces;
using AiLab.Infrastructure.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Console.Agents;

// Creates the triage agent for /agent (week 6 Friday). Its own chat pipeline and its own MCP connection, so nothing
// /saas or the normal chat uses is shared or changed.
public sealed class TriageAgentFactory : IAsyncDisposable
{
    private const string Name = "triage";
    private const string Description = "Handles support tickets for the SaaS by looking up recent deployments and the tenant's own data.";

    // Haiku thinks by default and the thinking counts toward this limit; the adapter's default 1,024 cut it off on Wednesday
    private const int MaxOutputTokens = 2000;

    private readonly IChatClient _chatClient;
    private readonly SaasMcpToolSource _saasTools;
    private readonly TicketTools _ticketTools;
    private readonly IChangeLogRepository _changeLog;
    private readonly TimeProvider _time;

    public TriageAgentFactory(IServiceProvider services, IConfiguration config, TicketTools ticketTools,
        IChangeLogRepository changeLog, TimeProvider time)
    {
        _ticketTools = ticketTools;
        _changeLog = changeLog;
        _time = time;

        // A second SaasMcpToolSource, not the singleton /saas uses: if /agent connected that one, a later /saas on would
        // see IsConnected and never give the chat its tools, and /saas off would cut the agent's tools mid-session.
        // The token provider underneath is still the shared singleton, so its cached tokens are reused.
        _saasTools = ActivatorUtilities.CreateInstance<SaasMcpToolSource>(services);

        // The agent's own pipeline over the bare provider. The first Use... is the outermost layer: function invocation
        // wraps logging, so every model call of the loop is logged (same order as Program.cs). Not the DI IChatClient,
        // whose cap of 3 belongs to /saas. Build(services) gives UseLogging the host's ILoggerFactory; without it,
        // logging would do nothing.
        _chatClient = new ChatClientBuilder(services.GetRequiredKeyedService<IChatClient>(ChatClientKeys.Provider))
            .UseFunctionInvocation(configure: f =>
            {
                // The rule that stops the agent: model calls with tool results, before a last answer without tools.
                // Agent__MaxToolIterations=1 overrides it for the step-limit test.
                f.MaximumIterationsPerRequest = config.GetValue("Agent:MaxToolIterations", 6);
                // A failing tool sends the model only a generic error, never the exception's text
                f.IncludeDetailedErrors = false;
            })
            .UseLogging()
            .Build(services);
    }

    public bool IsConnected => _saasTools.IsConnected;

    // Connects the agent's MCP client (initialize + tools/list; a stopped SaaS API or a bad login fails here) and
    // creates the agent with today's date in its instructions. Call once per session.
    public async Task<ChatClientAgent> CreateAsync(CancellationToken cancellationToken = default)
    {
        IList<AITool> saasTools = await _saasTools.ConnectAsync(cancellationToken);

        string instructions = TriageAgentInstructions.Build(
            DateOnly.FromDateTime(_time.GetLocalNow().DateTime), _changeLog.GetComponents());

        return _chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = Name,
            Description = Description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [_ticketTools.AsAIFunction(), .. saasTools],
                MaxOutputTokens = MaxOutputTokens,
            },
            // The pipeline above already has function invocation and logging. Without this, the agent would add its own
            // approval and OpenTelemetry layers around it (it skips its own FunctionInvokingChatClient only because it
            // finds ours).
            UseProvidedChatClientAsIs = true,
        });
    }

    // After this the MCP tools must not be sent to the model again, so the caller drops the agent and its session too
    public Task DisconnectAsync() => _saasTools.DisconnectAsync();

    public async ValueTask DisposeAsync()
    {
        await _saasTools.DisposeAsync();
        _chatClient.Dispose();
    }
}
