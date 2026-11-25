using AiLab.Domain.Interfaces;
using AiLab.Evals.Retrieval;
using AiLab.Infrastructure.KnowledgeBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Evals.Hosting;

// The Knowledge Base side of the evals (week 8 Tuesday): no Rag host, no Ollama, no Postgres. EvalFixture starts AiLab.Rag
// and probes /search, and the Rag host has no IKnowledgeBaseRetriever (AiLab.Rag doesn't change today), so the KB runner
// gets this small fixture: configuration from environment variables (KnowledgeBase__Id, KnowledgeBase__ModelArn, Bedrock__*),
// AddKnowledgeBase(config), resolve. Without AILAB_EVAL nothing is built, so plain `dotnet test` stays offline.
//   source ~/.aws/emulator/ai-lab-kb.env   (then the safety check)
//   AILAB_EVAL=1 KnowledgeBase__Id=$KB_ID Bedrock__ServiceUrl=https://127.0.0.1:9297 \
//   Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem dotnet test AiLab.Evals --filter KnowledgeBaseRetrievalEvalTests
public sealed class KnowledgeBaseFixture : IAsyncLifetime
{
    private ServiceProvider? _services;

    public EvalSettings Settings { get; } = EvalSettings.FromEnvironment();

    public IConfiguration Configuration { get; } = new ConfigurationBuilder().AddEnvironmentVariables().Build();

    // Where the KB runs, from the endpoint the SDK will use: the label and the result names follow it (Task 1.3)
    public KnowledgeBaseTarget Target { get; private set; } = KnowledgeBaseTarget.Bedrock;

    public IKnowledgeBaseRetriever Retriever => _services?.GetRequiredService<IKnowledgeBaseRetriever>() ?? throw NotStarted();

    public KnowledgeBaseSettings KnowledgeBase => _services?.GetRequiredService<KnowledgeBaseSettings>() ?? throw NotStarted();

    public IServiceProvider Services => _services ?? throw NotStarted();

    public PassageLocator Locator { get; } = new();

    public ValueTask InitializeAsync()
    {
        if (!Settings.Enabled)
            return ValueTask.CompletedTask;

        if (KnowledgeBaseSettings.From(Configuration) is null)
            throw new InvalidOperationException(
                "KnowledgeBase:Id is not set: run with KnowledgeBase__Id=$KB_ID (source ~/.aws/emulator/ai-lab-kb.env first on the emulator).");

        Target = KnowledgeBaseTarget.From(Configuration["Bedrock:ServiceUrl"], Environment.GetEnvironmentVariable);
        _services = new ServiceCollection().AddKnowledgeBase(Configuration).BuildServiceProvider();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
    }

    private static InvalidOperationException NotStarted() =>
        new("The KB fixture isn't started: skip the test first with Assert.SkipUnless(Settings.Enabled, EvalSettings.SkipReason).");
}

// Where a Knowledge Base run went, decided once from the endpoint, never from a name someone typed.
// Emulator: Bedrock:ServiceUrl is set, or an AWS_ENDPOINT_URL variable is (a sourced ai-lab-kb.env sets AWS_ENDPOINT_URL,
// and the SDK copies it into ServiceURL even when Bedrock:ServiceUrl is empty). Only otherwise is it Bedrock.
public sealed record KnowledgeBaseTarget(string Place, string Label)
{
    public static readonly KnowledgeBaseTarget Emulator = new("emulator", "KB on aws-emulator (measured, stand-in)");
    public static readonly KnowledgeBaseTarget Bedrock = new("bedrock", "KB on Bedrock (measured)");

    // The variables the AWS SDK reads for an endpoint override: the global one and the agent runtime's own
    public static readonly string[] EndpointVariables = ["AWS_ENDPOINT_URL", "AWS_ENDPOINT_URL_BEDROCK_AGENT_RUNTIME"];

    public static KnowledgeBaseTarget From(string? serviceUrl, Func<string, string?> env) =>
        !string.IsNullOrWhiteSpace(serviceUrl) || EndpointVariables.Any(v => !string.IsNullOrWhiteSpace(env(v)))
            ? Emulator
            : Bedrock;

    // Block 5's execution name and Block 4's file suffix: kb-emulator / kb-bedrock (Task 1.3)
    public string ExecutionName => $"kb-{Place}";
}
