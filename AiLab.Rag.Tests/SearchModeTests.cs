using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiLab.Application.Search;
using AiLab.Domain.Constants;
using AiLab.Domain.DTOs;
using AiLab.Domain.Enums;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;
using AiLab.Rag.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using static AiLab.Rag.Tests.Fakes.ScriptedChatClient;

namespace AiLab.Rag.Tests;

// Offline: GET /search?mode= and Ask:SearchMode over the in-memory store and InMemoryKeywordSearch (query words counted
// in the chunk text). Distances come from the hashed embedder: an exact copy of a chunk's text is at 0, anything else at about 1.
public sealed class SearchModeTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Question = "Which environment variable sets the JWT secret?";

    // Keyword order: Config (environment, variable, jwt, secret) then Rotation (jwt, secret); Reset shares no word
    private static readonly TextChunk Config = new(ChunkerNames.Heading, "README.md", 6, "Configuration: the environment variable Jwt__Secret holds the JWT secret.", "Configuration", 65, 76);
    private static readonly TextChunk Rotation = new(ChunkerNames.Heading, "docs/decisions.md", 7, "Rotate the JWT secret every quarter.", "Decisions", 29, 31);
    private static readonly TextChunk Reset = new(ChunkerNames.Heading, "docs/architecture.md", 4, "Password reset links expire after one hour.", "Architecture", 32, 36);

    private readonly List<RagFactory> _factories = [];

    public void Dispose()
    {
        foreach (RagFactory factory in _factories)
            factory.Dispose();
    }

    [Fact]
    public async Task Vector_is_the_default_and_is_sorted_by_distance()
    {
        using HttpClient client = await SeededClient();

        SearchResponse none = await Search(client, "");
        SearchResponse empty = await Search(client, "&mode=");
        SearchResponse vector = await Search(client, "&mode=vector");

        Assert.Equal(3, vector.Hits.Count);
        Assert.Equal(vector.Hits.Select(h => h.Distance).Order(), vector.Hits.Select(h => h.Distance));
        Assert.Equal(vector.Hits.Select(h => h.Key), none.Hits.Select(h => h.Key));
        Assert.Equal(vector.Hits.Select(h => h.Key), empty.Hits.Select(h => h.Key));
    }

    [Fact]
    public async Task Keyword_orders_by_shared_words_and_carries_the_distance()
    {
        using HttpClient client = await SeededClient();

        SearchResponse keyword = await Search(client, "&mode=keyword");
        SearchResponse vector = await Search(client, "&mode=vector");

        // Reset shares no word with the question, so it isn't returned
        Assert.Equal([Config.Key, Rotation.Key], keyword.Hits.Select(h => h.Key));
        Assert.Equal([1, 2], keyword.Hits.Select(h => h.Rank));
        foreach (SearchResponseHit hit in keyword.Hits)
            Assert.Equal(vector.Hits.Single(v => v.Key == hit.Key).Distance, hit.Distance, 6);
    }

    [Fact]
    public async Task Hybrid_is_the_rank_fusion_of_the_vector_and_keyword_lists()
    {
        using HttpClient client = await SeededClient();

        SearchResponse vector = await Search(client, "&mode=vector", top: 20);
        SearchResponse keyword = await Search(client, "&mode=keyword", top: 20);
        SearchResponse hybrid = await Search(client, "&mode=hybrid", top: 5);

        IReadOnlyList<SearchHit> expected = RankFusion.Merge(Hits(vector), Hits(keyword), top: 5);
        Assert.Equal(expected.Select(h => h.Chunk.Key), hybrid.Hits.Select(h => h.Key));
        // Reset is found by vector search only, so it comes after the two chunks both searches found
        Assert.Equal(Reset.Key, hybrid.Hits[^1].Key);
    }

    [Fact]
    public async Task An_unknown_mode_is_a_400()
    {
        using HttpClient client = await SeededClient();

        HttpResponseMessage response = await client.GetAsync($"/search?q={Uri.EscapeDataString(Question)}&mode=semantic", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal(ErrorCodes.InvalidRequest, error.Code);
        Assert.Contains("vector, keyword, hybrid", error.Error);
    }

    // Keyword (and hybrid) lists aren't sorted by distance. With keyword, the first hit here is far and the second is an exact
    // copy: gating on hits[0] would abstain (too far), so this case fails on the old gate. With hybrid, the tie goes to the
    // closer chunk and the exact copy comes first anyway: that case only checks the mode runs end to end.
    [Theory]
    [InlineData(SearchModes.Keyword)]
    [InlineData(SearchModes.Hybrid)]
    public async Task Ask_gates_on_the_smallest_distance_not_the_first_hit(string mode)
    {
        // Both chunks hold every word of the question, so keyword search ties them and the key decides: a.md first
        const string question = "Tenants are resolved from the JWT tenant_id claim.";
        TextChunk far = new(ChunkerNames.Heading, "a.md", 1, question + " Rotated yearly.", null, 1, 2);
        TextChunk exact = new(ChunkerNames.Heading, "b.md", 1, question, null, 1, 2);

        ScriptedChatClient script = new(Reply(JsonSerializer.Serialize(new { answer = "From the JWT tenant_id claim [1].", citedSources = new[] { 1 }, notInDocs = false })));
        RagFactory factory = Factory(script, new() { ["Ask:SearchMode"] = mode });
        await Index(factory, far, exact);
        using HttpClient client = factory.CreateClient();

        if (mode == SearchModes.Keyword)
        {
            SearchResponse keyword = (await client.GetFromJsonAsync<SearchResponse>($"/search?q={Uri.EscapeDataString(question)}&mode=keyword&chunker=heading", Ct))!;
            Assert.Equal([far.Key, exact.Key], keyword.Hits.Select(h => h.Key));
            Assert.True(keyword.Hits[0].Distance > 0.5);
        }

        HttpResponseMessage response = await client.PostAsJsonAsync("/ask", new { question }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AskResponse ask = (await response.Content.ReadFromJsonAsync<AskResponse>(Ct))!;

        Assert.Equal(GateResult.Passed, ask.Retrieval.Gate);
        Assert.Equal(0, ask.Retrieval.BestDistance!.Value, 4);
        Assert.Single(script.Calls);
    }

    [Fact]
    public void An_unknown_Ask_SearchMode_stops_the_host_at_startup()
    {
        RagFactory factory = Factory(new ScriptedChatClient(), new() { ["Ask:SearchMode"] = "semantic" });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Ask:SearchMode 'semantic'", error.Message);
    }

    private RagFactory Factory(ScriptedChatClient? script = null, Dictionary<string, string?>? settings = null)
    {
        var factory = new RagFactory(new HashedEmbeddingGenerator(), settings: settings, chat: script);
        _factories.Add(factory);
        return factory;
    }

    private async Task<HttpClient> SeededClient()
    {
        RagFactory factory = Factory();
        await Index(factory, Config, Rotation, Reset);
        return factory.CreateClient();
    }

    private static Task Index(RagFactory factory, params TextChunk[] chunks) =>
        factory.Services.GetRequiredService<IVectorSearchService>().IndexAsync(chunks, Ct);

    private static async Task<SearchResponse> Search(HttpClient client, string modeQuery, int top = 5)
    {
        HttpResponseMessage response = await client.GetAsync($"/search?q={Uri.EscapeDataString(Question)}&top={top}&chunker=heading{modeQuery}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(Ct))!;
    }

    private static List<SearchHit> Hits(SearchResponse response) =>
        [.. response.Hits.Select(h => new SearchHit(new TextChunk(h.Chunker, h.Source, h.Position, h.Text, h.Heading, h.StartLine, h.EndLine), h.Distance))];
}
