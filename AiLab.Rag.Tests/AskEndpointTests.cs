using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;
using AiLab.Infrastructure.Files;
using AiLab.Rag.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using static AiLab.Rag.Tests.Fakes.ScriptedChatClient;

namespace AiLab.Rag.Tests;

// POST /ask offline: the in-memory store, the hashed embedder and a scripted chat model. The real AskService, prompt file,
// CitationCheck, rate limiter and error mapping run.
// Distances come from the hashed embedder: a question that is exactly a chunk's text is at distance 0 from it (the gate
// passes), and unrelated texts are at about 1 (random 768-number vectors), far above Ask:MaxDistance 0.5.
public sealed class AskEndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Three heading chunks; the question "Tenants are resolved..." matches the first exactly
    private static readonly TextChunk Tenancy = new(ChunkerNames.Heading, "docs/architecture.md", 3, "Tenants are resolved from the JWT tenant_id claim.", "Architecture > Tenancy", 20, 24);
    private static readonly TextChunk Billing = new(ChunkerNames.Heading, "docs/decisions.md", 1, "Billing callbacks are signed with a timestamped HMAC.", "Decisions > Contracts", 15, 17);
    private static readonly TextChunk Setup = new(ChunkerNames.Heading, "README.md", 0, "Run docker compose up -d before the Postgres tests.", null, 5, 6);

    private const string OnTopic = "Tenants are resolved from the JWT tenant_id claim.";

    private readonly List<RagFactory> _factories = [];

    public void Dispose()
    {
        foreach (RagFactory factory in _factories)
            factory.Dispose();
    }

    [Fact]
    public async Task A_cited_answer_is_grounded_with_citations_from_the_server_map()
    {
        ScriptedChatClient script = new(Reply(Answer("Tenants come from the JWT's tenant_id claim [1].", [1])));
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal("Tenants come from the JWT's tenant_id claim [1].", ask.Answer);
        Assert.Equal(GroundingStatus.Grounded, ask.Grounding.Status);
        AskCitation citation = Assert.Single(ask.Citations);
        Assert.Equal((1, "docs/architecture.md", "Architecture > Tenancy", 20, 24), (citation.N, citation.Source, citation.Heading, citation.StartLine, citation.EndLine));
        Assert.Equal(0, citation.Distance, 4);

        Assert.Equal((ChunkerNames.Heading, 5, GateResult.Passed), (ask.Retrieval.Chunker, ask.Retrieval.Top, ask.Retrieval.Gate));
        Assert.Equal(0, ask.Retrieval.BestDistance!.Value, 4);
        Assert.Equal(("scripted-model", 200L, 50L), (ask.Usage.Model, ask.Usage.InputTokens, ask.Usage.OutputTokens));
        Assert.NotNull(ask.Timings.GenerationMs);
        Assert.Single(script.Calls);
    }

    [Fact]
    public async Task The_prompt_is_the_file_plus_numbered_sources_and_the_question()
    {
        ScriptedChatClient script = new(Reply(Answer("Tenants come from the JWT [1].", [1])));
        using HttpClient client = await SeededClient(script);

        await AskOk(client, new { question = $"  {OnTopic}  " });

        (List<ChatMessage> messages, ChatOptions? options) = Assert.Single(script.Calls);
        string instructions = await File.ReadAllTextAsync(RepoPaths.Resolve("prompts/ask-v1.md"), Ct);
        Assert.Equal(instructions, messages.Single(m => m.Role == ChatRole.System).Text);
        Assert.Equal(800, options?.MaxOutputTokens);

        // All three chunks were sent (top 5, three stored), the exact match first; the question last, trimmed
        string user = messages.First(m => m.Role == ChatRole.User).Text;
        Assert.StartsWith("<source n=\"1\" file=\"docs/architecture.md\" section=\"Architecture > Tenancy\" lines=\"20-24\">", user);
        Assert.Contains("<source n=\"3\"", user);
        Assert.DoesNotContain("<source n=\"4\"", user);
        Assert.Contains("section=\"\"", user); // README's chunk has no heading
        Assert.EndsWith($"Question: {OnTopic}", user);
    }

    [Fact]
    public async Task An_unrelated_question_abstains_at_the_gate_without_a_model_call()
    {
        ScriptedChatClient script = new();
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = "What is the best recipe for banana bread?" });

        Assert.Equal(GroundingStatus.Abstained, ask.Grounding.Status);
        Assert.Contains("farther than 0.5", ask.Grounding.Reason);
        Assert.Equal(GateResult.TooFar, ask.Retrieval.Gate);
        Assert.True(ask.Retrieval.BestDistance > 0.5);
        Assert.Equal("", ask.Answer);
        Assert.Empty(ask.Citations);
        Assert.Equal(new AskUsage(null, null, null), ask.Usage);
        Assert.Null(ask.Timings.GenerationMs);
        Assert.Empty(script.Calls);
    }

    [Fact]
    public async Task No_heading_chunks_abstains_as_no_hits_without_a_model_call()
    {
        ScriptedChatClient script = new();
        RagFactory factory = Factory(script);
        // The collection exists, but holds only a seed sentence (chunker "lines"), which /ask doesn't search
        await Index(factory, new TextChunk(ChunkerNames.Lines, "similarity-sentences.txt", 1, OnTopic, null, 1, 1));
        using HttpClient client = factory.CreateClient();

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal((GroundingStatus.Abstained, GateResult.NoHits), (ask.Grounding.Status, ask.Retrieval.Gate));
        Assert.Null(ask.Retrieval.BestDistance);
        Assert.Empty(script.Calls);
    }

    [Fact]
    public async Task A_near_miss_abstains_through_notInDocs_and_drops_citations()
    {
        ScriptedChatClient script = new(Reply(Answer("The documentation doesn't cover which email provider is used.", [2], notInDocs: true)));
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal(GroundingStatus.Abstained, ask.Grounding.Status);
        Assert.Equal(GateResult.Passed, ask.Retrieval.Gate);
        Assert.Equal("The documentation doesn't cover which email provider is used.", ask.Answer);
        Assert.Empty(ask.Citations);
        Assert.Equal(200, ask.Usage.InputTokens); // the model was called
    }

    [Fact]
    public async Task Citing_a_number_that_was_not_sent_is_unverified()
    {
        // Three sources were sent, so [7] is made up
        ScriptedChatClient script = new(Reply(Answer("Tenants come from the JWT [1], per the ADR [7].", [1, 7])));
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal(GroundingStatus.Unverified, ask.Grounding.Status);
        Assert.Contains("[7]", ask.Grounding.Reason);
        Assert.Equal([1], ask.Citations.Select(c => c.N));
    }

    [Fact]
    public async Task A_fenced_reply_is_read()
    {
        ScriptedChatClient script = new(Reply($"```json\n{Answer("Tenants come from the JWT [1].", [1])}\n```"));
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal(GroundingStatus.Grounded, ask.Grounding.Status);
        Assert.Single(script.Calls);
    }

    [Theory]
    [InlineData("Tenants come from the JWT [1].", "it didn't parse")]
    [InlineData("""{ "answer": "", "citedSources": [], "notInDocs": false }""", "answer is empty")]
    // What llama3.2 sent under Ollama's JSON mode for "What does tenant registration return?": the unescaped quote before
    // pending_verification ended the answer, and the object was closed there (valid JSON, no citedSources)
    [InlineData("{\"answer\": \"A successful registration returns 202 Accepted with { tenantId, status: \"      \n              }", "citedSources is missing")]
    public async Task An_unusable_reply_gets_one_repair_call_naming_the_problem(string firstReply, string problem)
    {
        ScriptedChatClient script = new(Reply(firstReply), Reply(Answer("Tenants come from the JWT [1].", [1])));
        using HttpClient client = await SeededClient(script);

        AskResponse ask = await AskOk(client, new { question = OnTopic });

        Assert.Equal(GroundingStatus.Grounded, ask.Grounding.Status);
        Assert.Equal(2, script.Calls.Count);
        // Not the last message: GetResponseAsync<T> appends its schema message after the conversation
        Assert.Contains(script.Calls[1].Messages, m => m.Role == ChatRole.User && m.Text.Contains($"wasn't valid JSON for the schema ({problem}"));
        Assert.Equal((400L, 100L), (ask.Usage.InputTokens!.Value, ask.Usage.OutputTokens!.Value)); // both calls counted
    }

    [Fact]
    public async Task A_reply_still_unusable_after_the_repair_is_a_502_reply_unreadable()
    {
        ScriptedChatClient script = new(Reply("not json"), Reply("{ still not json"));
        using HttpClient client = await SeededClient(script);

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.BadGateway, ErrorCodes.ReplyUnreadable);
        Assert.Equal(2, script.Calls.Count);
    }

    [Fact]
    public async Task A_cut_off_reply_is_a_502_reply_cut_off_without_a_repair()
    {
        ScriptedChatClient script = new(Reply("""{ "answer": "Tenants come from the J""", ChatFinishReason.Length));
        using HttpClient client = await SeededClient(script);

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.BadGateway, ErrorCodes.ReplyCutOff);
        Assert.Single(script.Calls);
    }

    [Fact]
    public async Task A_content_filter_stop_is_a_502_reply_unreadable()
    {
        ScriptedChatClient script = new(Reply("", ChatFinishReason.ContentFilter));
        using HttpClient client = await SeededClient(script);

        ErrorResponse error = await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.BadGateway, ErrorCodes.ReplyUnreadable);
        Assert.Contains("declined", error.Error);
    }

    // The chat provider failing must not read as the embedding provider: both wrap an HttpRequestException
    public static TheoryData<Exception> ProviderFailures => new()
    {
        new HttpRequestException("Connection refused (localhost:11434)", new SocketException((int)SocketError.ConnectionRefused)),
        new TaskCanceledException("The request timed out."), // an SDK timeout: a cancellation nobody asked for
    };

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task A_chat_provider_failure_is_a_502_provider_unavailable(Exception failure)
    {
        ScriptedChatClient script = new(Throw(failure));
        using HttpClient client = await SeededClient(script);

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.BadGateway, ErrorCodes.ProviderUnavailable);
    }

    [Fact]
    public async Task An_embedding_failure_is_a_502_embedding_unavailable_before_any_model_call()
    {
        ScriptedChatClient script = new();
        using HttpClient client = Factory(script, new HashedEmbeddingGenerator(new HttpRequestException("Connection refused"))).CreateClient();

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.BadGateway, ErrorCodes.EmbeddingUnavailable);
        Assert.Empty(script.Calls);
    }

    [Fact]
    public async Task Asking_before_ingesting_is_a_409_collection_missing()
    {
        using HttpClient client = Factory(new ScriptedChatClient()).CreateClient();

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.Conflict, ErrorCodes.CollectionMissing);
    }

    // Offline: nothing listens on port 1, so the connection is refused at once
    [Fact]
    public async Task An_unreachable_Postgres_is_a_503_vector_store_unavailable()
    {
        using HttpClient client = Factory(new ScriptedChatClient(), settings: new()
        {
            ["VectorStore:Provider"] = "postgres",
            ["ConnectionStrings:Vectors"] = "Host=localhost;Port=1;Database=ailab;Username=ailab;Password=ailab;Timeout=2",
        }).CreateClient();

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.ServiceUnavailable, ErrorCodes.VectorStoreUnavailable);
    }

    [Fact]
    public async Task A_missing_prompt_file_is_a_500_internal_error()
    {
        ScriptedChatClient script = new();
        RagFactory factory = Factory(script, settings: new() { ["Ask:PromptPath"] = "prompts/does-not-exist.md" });
        await Index(factory, Tenancy);
        using HttpClient client = factory.CreateClient();

        await AssertError(await Ask(client, new { question = OnTopic }), HttpStatusCode.InternalServerError, ErrorCodes.InternalError);
        Assert.Empty(script.Calls);
    }

    [Theory]
    [InlineData("""{ "question": "" }""", "Question is required.")]
    [InlineData("""{ "question": "   " }""", "Question is required.")]
    [InlineData("""{ }""", "Question is required.")]
    [InlineData("""{ "question": "tenants", "top": 0 }""", "top must be between 1 and 10.")]
    [InlineData("""{ "question": "tenants", "top": 11 }""", "top must be between 1 and 10.")]
    [InlineData("""{ "question": "tenants", "top": "5" }""", "The request could not be read.")] // strict number handling
    [InlineData("""not json""", "The request could not be read.")]
    public async Task Invalid_requests_are_a_400_before_any_embedding_or_model_call(string body, string message)
    {
        var generator = new HashedEmbeddingGenerator();
        ScriptedChatClient script = new();
        using HttpClient client = Factory(script, generator).CreateClient();

        ErrorResponse error = await AssertError(await AskRaw(client, body), HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);

        Assert.Equal(message, error.Error);
        Assert.Empty(generator.BatchSizes);
        Assert.Empty(script.Calls);
    }

    [Fact]
    public async Task A_question_over_500_characters_is_a_400()
    {
        using HttpClient client = Factory(new ScriptedChatClient()).CreateClient();

        ErrorResponse error = await AssertError(await Ask(client, new { question = new string('a', 501) }), HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);

        Assert.Equal("Question must be at most 500 characters.", error.Error);
    }

    [Fact]
    public async Task Over_the_limit_is_a_429_rate_limited_with_Retry_After()
    {
        // Invalid requests count too, so the limit is reached without any model call
        using HttpClient client = Factory(new ScriptedChatClient(), settings: new() { ["Ask:PermitsPerMinute"] = "2" }).CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await Ask(client, new { question = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Ask(client, new { question = "" })).StatusCode);
        HttpResponseMessage third = await Ask(client, new { question = "" });

        ErrorResponse error = await AssertError(third, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        Assert.Contains("at most 2 per minute", error.Error);
        Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task The_limit_is_only_on_ask()
    {
        using HttpClient client = Factory(new ScriptedChatClient(), settings: new() { ["Ask:PermitsPerMinute"] = "1" }).CreateClient();

        await Ask(client, new { question = "" });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/search?q=", Ct)).StatusCode); // not 429
    }

    private RagFactory Factory(ScriptedChatClient script, HashedEmbeddingGenerator? generator = null, Dictionary<string, string?>? settings = null)
    {
        // Disposing the client doesn't stop the host, so the factories are disposed with the test class
        var factory = new RagFactory(generator ?? new HashedEmbeddingGenerator(), settings: settings, chat: script);
        _factories.Add(factory);
        return factory;
    }

    private async Task<HttpClient> SeededClient(ScriptedChatClient script)
    {
        RagFactory factory = Factory(script);
        await Index(factory, Tenancy, Billing, Setup);
        return factory.CreateClient();
    }

    private static Task Index(RagFactory factory, params TextChunk[] chunks) =>
        factory.Services.GetRequiredService<IVectorSearchService>().IndexAsync(chunks, Ct);

    private static string Answer(string answer, int[] cited, bool notInDocs = false) =>
        JsonSerializer.Serialize(new { answer, citedSources = cited, notInDocs });

    private static Task<HttpResponseMessage> Ask(HttpClient client, object body) => client.PostAsJsonAsync("/ask", body, Ct);

    private static Task<HttpResponseMessage> AskRaw(HttpClient client, string body) =>
        client.PostAsync("/ask", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

    private static async Task<AskResponse> AskOk(HttpClient client, object body)
    {
        HttpResponseMessage response = await Ask(client, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AskResponse>(Ct))!;
    }

    private static async Task<ErrorResponse> AssertError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal(code, error.Code);
        return error;
    }
}
