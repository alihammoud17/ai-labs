using System.Net;
using System.Net.Http.Json;
using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Responses;
using AiLab.Rag.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Rag.Tests;

// Offline: the in-memory store and a fake generator (one fixed vector per text), no Ollama, no Docker.
// The seed file is a temporary copy of ten sentences with blank lines between some of them.
public sealed class SearchEndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] Sentences =
    [
        "Each tenant's data is stored in an isolated schema.",
        "Users can only access resources that belong to their tenant.",
        "API keys are scoped to a single tenant.",
        "Caching is enabled.",
        "Caching is not enabled.",
        "Responses are cached in Redis for five minutes.",
        "Invoices are generated at the end of each billing period.",
        "Usage above the plan limit is charged per request.",
        "No customer can ever see another customer's records.",
        "The car needs a new head gasket.",
    ];

    private readonly string _seedPath = Path.Combine(Path.GetTempPath(), $"rag-seed-{Guid.NewGuid():N}.txt");
    private readonly List<RagFactory> _factories = [];

    public SearchEndpointTests() =>
        // Blank and whitespace lines are skipped, but still counted in the line numbers
        File.WriteAllLines(_seedPath, [Sentences[0], "", .. Sentences[1..5], "   ", .. Sentences[5..]]);

    public void Dispose()
    {
        foreach (RagFactory factory in _factories)
            factory.Dispose();
        File.Delete(_seedPath);
    }

    [Fact]
    public async Task Seed_then_search_ranks_by_distance_and_an_exact_copy_comes_first_at_about_zero()
    {
        var generator = new HashedEmbeddingGenerator();
        using HttpClient client = Client(generator);

        SeedResponse seed = await Seed(client);
        Assert.Equal(10, seed.Indexed);
        Assert.Equal(Path.GetFileName(_seedPath), seed.Source);
        Assert.Equal("inmemory", seed.Provider);
        Assert.Equal([10], generator.BatchSizes); // one embedding call for the ten chunks

        SearchResponse search = await Search(client, "Caching is enabled.", top: 5);

        Assert.Equal("inmemory", search.Provider);
        Assert.Equal(5, search.Hits.Count);
        Assert.Equal([1, 2, 3, 4, 5], search.Hits.Select(h => h.Rank));
        Assert.Equal(search.Hits.Select(h => h.Distance).Order(), search.Hits.Select(h => h.Distance));

        SearchResponseHit first = search.Hits[0];
        Assert.Equal("Caching is enabled.", first.Text);
        Assert.Equal(0, first.Distance, 4);
        Assert.Equal(1, first.Similarity, 4);
        Assert.Equal(Math.Round(1 - search.Hits[1].Distance, 4), search.Hits[1].Similarity);
    }

    [Fact]
    public async Task Keys_follow_the_line_numbers_of_the_file()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator());
        await Seed(client);

        SearchResponse search = await Search(client, "Caching is enabled.", top: 1);

        // Line 1 is a sentence, line 2 blank, so "Caching is enabled." (the 4th sentence) is on line 5
        string prefix = Path.GetFileNameWithoutExtension(_seedPath);
        Assert.Equal($"{prefix}:5", search.Hits[0].Key);
    }

    [Fact]
    public async Task Seeding_twice_replaces_the_rows()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator());

        await Seed(client);
        await Seed(client);

        SearchResponse search = await Search(client, "anything", top: 20);
        Assert.Equal(10, search.Hits.Count);
    }

    [Theory]
    [InlineData("/search?q=")]
    [InlineData("/search?q=%20%20")]
    [InlineData("/search")]
    [InlineData("/search?q=caching&top=0")]
    [InlineData("/search?q=caching&top=50")]
    public async Task Invalid_searches_are_a_400_invalid_request(string url)
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator());
        await Seed(client);

        HttpResponseMessage response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
    }

    [Fact]
    public async Task Search_before_seeding_is_a_409_that_says_to_seed()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator());

        HttpResponseMessage response = await client.GetAsync("/search?q=caching", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("collection_missing", error.Code);
        Assert.Contains("/dev/seed", error.Error);
    }

    // dotnet run starts the host in AiLab.Rag/, so the default path must come from the repo root, not the working directory
    [Fact]
    public async Task The_default_seed_path_is_the_repo_file()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator(), settings: new() { ["Seed:Path"] = "" });

        SeedResponse seed = await Seed(client);

        Assert.Equal("similarity-sentences.txt", seed.Source);
        Assert.True(seed.Indexed > 0);
    }

    [Fact]
    public async Task A_400_message_is_a_plain_sentence()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator());

        HttpResponseMessage response = await client.GetAsync("/search?q=caching&top=0", Ct);

        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("top must be between 1 and 20.", error.Error);
    }

    [Fact]
    public async Task A_missing_seed_file_is_a_400_with_the_path()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no-such-seed-file.txt");
        using HttpClient client = Client(new HashedEmbeddingGenerator(), settings: new() { ["Seed:Path"] = missing });

        HttpResponseMessage response = await client.PostAsync("/dev/seed", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("seed_file_not_found", error.Code);
        Assert.Contains(missing, error.Error);
    }

    [Theory]
    [InlineData(null, "Is Ollama running?")]              // nothing answered (connection refused)
    [InlineData(HttpStatusCode.NotFound, "ollama pull")]  // the model isn't pulled
    public async Task An_embedding_provider_failure_is_a_502_with_a_hint(HttpStatusCode? status, string hint)
    {
        var failing = new HashedEmbeddingGenerator(new HttpRequestException("embedding failed", null, status));
        using HttpClient client = Client(failing);

        HttpResponseMessage response = await client.PostAsync("/dev/seed", null, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("embedding_unavailable", error.Code);
        Assert.Contains(hint, error.Error);
    }

    // Offline too: nothing listens on port 1, so the connection is refused at once
    [Fact]
    public async Task An_unreachable_Postgres_is_a_503_with_a_hint()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator(), settings: new()
        {
            ["VectorStore:Provider"] = "postgres",
            ["ConnectionStrings:Vectors"] = "Host=localhost;Port=1;Database=ailab;Username=ailab;Password=ailab;Timeout=2",
        });

        HttpResponseMessage response = await client.PostAsync("/dev/seed", null, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("vector_store_unavailable", error.Code);
        Assert.Contains("docker compose up -d", error.Error);
    }

    [Fact]
    public async Task Seed_is_not_mapped_outside_Development()
    {
        using HttpClient client = Client(new HashedEmbeddingGenerator(), environment: "Testing");

        HttpResponseMessage response = await client.PostAsync("/dev/seed", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Indexing_nothing_returns_zero_and_never_calls_the_generator()
    {
        var generator = new HashedEmbeddingGenerator();
        using var factory = new RagFactory(generator, settings: new() { ["Seed:Path"] = _seedPath });

        int indexed = await factory.Services.GetRequiredService<IVectorSearchService>().IndexAsync(Array.Empty<TextChunk>(), Ct);

        Assert.Equal(0, indexed);
        Assert.Empty(generator.BatchSizes);
    }

    private HttpClient Client(HashedEmbeddingGenerator generator, string environment = "Development", Dictionary<string, string?>? settings = null)
    {
        Dictionary<string, string?> all = new() { ["Seed:Path"] = _seedPath };
        foreach ((string key, string? value) in settings ?? [])
            all[key] = value;
        // Disposing the client doesn't stop the host, so the factories are disposed with the test class
        var factory = new RagFactory(generator, environment, all);
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static async Task<SeedResponse> Seed(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync("/dev/seed", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeedResponse>(Ct))!;
    }

    private static async Task<SearchResponse> Search(HttpClient client, string query, int top)
    {
        HttpResponseMessage response = await client.GetAsync($"/search?q={Uri.EscapeDataString(query)}&top={top}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(Ct))!;
    }
}
