using System.Net.Http.Json;
using AiLab.Domain.DTOs;
using AiLab.Domain.Responses;
using AiLab.Domain.Settings;
using AiLab.Evals.Judging;
using AiLab.Infrastructure.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Evals.Hosting;

// One eval host per test class (IClassFixture<EvalFixture>). Without AILAB_EVAL nothing starts, so plain
// `dotnet test` stays offline: the tests skip before they touch any property here.
public sealed class EvalFixture : IAsyncLifetime
{
    private EvalRagFactory? _factory;
    private HttpClient? _http;
    private Lazy<IChatClient>? _judge;
    private IChatClient? _ollama;

    public EvalSettings Settings { get; } = EvalSettings.FromEnvironment();

    // /ask's generation goes wherever Chat.Target points (set per iteration)
    public ScenarioChatClient Chat { get; } = new();

    public HttpClient Http => _http ?? throw NotStarted();

    // The Rag host's own Ask:* values, so /search asks for exactly the chunks /ask sends
    public AskSettings Ask { get; private set; } = new();

    // Haiku 5.5 through HaikuJudgeOptionsClient (llama3.2 with AILAB_EVAL_JUDGE=llama, for free wiring checks).
    // Give it to the ReportingConfiguration (EvalReporting.Create); MEAI then puts its cache in front of it.
    // Created on first use, so the retrieval runner (no judge) needs no Anthropic key.
    public IChatClient Judge => (_judge ?? throw NotStarted()).Value;

    // llama3.2 for AILAB_EVAL_ANSWERS=llama: the host's Chat:* settings, not cached by MEAI
    public IChatClient Ollama => _ollama ?? throw NotStarted();

    public async ValueTask InitializeAsync()
    {
        if (!Settings.Enabled)
            return;

        _factory = new EvalRagFactory(Chat);
        _http = _factory.CreateClient();
        Ask = _factory.Services.GetRequiredService<AskSettings>();

        IConfiguration config = _factory.Services.GetRequiredService<IConfiguration>();
        _ollama = ChatClientFactory.Create("ollama", config["Chat:Model"] ?? "llama3.2", ollamaUrl: config["Chat:OllamaUrl"]);
        _judge = new Lazy<IChatClient>(() => CreateJudge(config));

        // Fail once, clearly, rather than 30 times with a 409 or a 503
        HttpResponseMessage probe = await Http.GetAsync($"/search?q=health&top=1&chunker={Ask.Chunker}");
        if (!probe.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GET /search answered {(int)probe.StatusCode}: {await probe.Content.ReadAsStringAsync()} " +
                "Start Postgres (colima start; docker compose up -d) and Ollama, and ingest the corpus (POST /dev/ingest?chunker=heading).");
    }

    private IChatClient CreateJudge(IConfiguration config)
    {
        if (Settings.Judge == "llama")
            return ChatClientFactory.Create("ollama", config["Chat:Model"] ?? "llama3.2", ollamaUrl: config["Chat:OllamaUrl"]);

        string apiKey = config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is missing from user-secrets: the judge is Haiku 5.5.");
        // 60 s per attempt, 2 retries: the SDK default (10 min per attempt) let one stuck call hold the llama run for ~35 min
        return new HaikuJudgeOptionsClient(ChatClientFactory.Create("anthropic", EvalSettings.HaikuModel, apiKey,
            anthropicAttemptTimeout: TimeSpan.FromSeconds(60), anthropicMaxRetries: 2));
    }

    // The chunks /ask will send for this question: same question, top and chunker as AskService uses
    // (top: a week 4 run asked with top 10, for the judge check; mode: vector, keyword or hybrid, null = Ask:SearchMode)
    public async Task<SearchResponse> SearchAsync(string question, CancellationToken ct, int? top = null, string? mode = null)
    {
        // Ask:SearchMode by default, so the judges see the chunks /ask sent
        string url = $"/search?q={Uri.EscapeDataString(question)}&top={top ?? Ask.Top}&chunker={Ask.Chunker}&mode={Uri.EscapeDataString(mode ?? Ask.SearchMode)}";
        return (await Http.GetFromJsonAsync<SearchResponse>(url, ct))!;
    }

    // POST /ask. Throws on any non-200: in an eval, an error status is a broken setup, not a low score.
    public async Task<AskResponse> AskAsync(string question, CancellationToken ct)
    {
        HttpResponseMessage response = await Http.PostAsJsonAsync("/ask", new AskRequest(question), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST /ask answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        return (await response.Content.ReadFromJsonAsync<AskResponse>(ct))!;
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_judge is { IsValueCreated: true })
            _judge.Value.Dispose();
        _ollama?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    private static InvalidOperationException NotStarted() =>
        new("The eval host isn't started: skip the test first with Assert.SkipUnless(Settings.Enabled, EvalSettings.SkipReason).");
}
