using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.VectorStore;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace AiLab.Api.Tests;

// Offline over a real in-memory collection. The record has 768 dimensions, so the vectors are 768 long but only
// use the first two axes: e0 = [1, 0, ...], e1 = [0, 1, ...], and halfway between them (45°, cosine 0.7071).
public class TextVectorStoreTests
{
    // A property, not a field: each test has its own token
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_returns_closest_first_and_an_identical_vector_scores_about_zero()
    {
        ITextVectorStore store = await InMemoryStore();
        await UpsertThree(store);

        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Vector(1, 0), top: 3, Ct);

        Assert.Equal(["same", "halfway", "orthogonal"], hits.Select(h => h.Key));
        Assert.Equal(0, hits[0].Score, 4);                  // cosine distance of identical vectors
        Assert.Equal(1 - Math.Sqrt(0.5), hits[1].Score, 4); // 1 - cos 45°
        Assert.Equal(1, hits[2].Score, 4);                  // orthogonal
    }

    [Fact]
    public async Task Top_limits_the_number_of_hits()
    {
        ITextVectorStore store = await InMemoryStore();
        await UpsertThree(store);

        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Vector(1, 0), top: 1, Ct);

        Assert.Equal("same", Assert.Single(hits).Key);
    }

    [Fact]
    public async Task Upserting_the_same_key_replaces_the_row()
    {
        ITextVectorStore store = await InMemoryStore();
        await store.UpsertAsync([new TextChunk("a", "old text", "doc.md")], [Vector(1, 0)], Ct);
        await store.UpsertAsync([new TextChunk("a", "new text", "doc.md")], [Vector(1, 0)], Ct);

        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Vector(1, 0), top: 10, Ct);

        Assert.Equal("new text", Assert.Single(hits).Text);
    }

    [Fact]
    public async Task Different_counts_of_chunks_and_embeddings_are_refused()
    {
        ITextVectorStore store = await InMemoryStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(
            [new TextChunk("a", "one", "doc.md"), new TextChunk("b", "two", "doc.md")], [Vector(1, 0)], Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Top_below_one_is_refused(int top)
    {
        ITextVectorStore store = await InMemoryStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SearchAsync(Vector(1, 0), top, Ct));
    }

    // In memory, a wrong-size row used to be accepted and then break every later search in the collection
    [Fact]
    public async Task A_wrong_size_embedding_is_refused_before_it_reaches_the_store()
    {
        ITextVectorStore store = await InMemoryStore();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => store.UpsertAsync([new TextChunk("small", "from all-minilm", "doc.md")], [new float[384]], Ct));
        Assert.Contains("384", error.Message);
        Assert.Contains("Embedding:Model", error.Message);

        await UpsertThree(store);
        Assert.Equal(3, (await store.SearchAsync(Vector(1, 0), top: 5, Ct)).Count);
    }

    [Fact]
    public async Task A_wrong_size_query_is_refused()
    {
        ITextVectorStore store = await InMemoryStore();
        await UpsertThree(store);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(new float[384], top: 5, Ct));
        Assert.Contains("Embedding:Model", error.Message);
    }

    // A zero vector has no direction, so its cosine distance is NaN; it must not come back as a score
    [Fact]
    public async Task A_NaN_score_is_refused()
    {
        ITextVectorStore store = await InMemoryStore();
        await UpsertThree(store);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SearchAsync(Vector(0, 0), top: 5, Ct));
        Assert.Contains("NaN", error.Message);
    }

    [Fact]
    public void AddVectorStore_inmemory_resolves_the_store_and_passes_startup_validation()
    {
        using ServiceProvider services = BuildServices(new() { ["VectorStore:Provider"] = "inmemory" });

        Assert.IsType<TextVectorStore>(services.GetRequiredService<ITextVectorStore>());
        Assert.Equal("chunks", services.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>().Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddVectorStore_without_a_provider_names_the_key(string? provider)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BuildServices(new() { ["VectorStore:Provider"] = provider }));

        Assert.Contains("VectorStore:Provider", error.Message);
    }

    [Fact]
    public void AddVectorStore_with_an_unknown_provider_names_the_key_and_the_allowed_values()
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => BuildServices(new() { ["VectorStore:Provider"] = "qdrant" }));

        Assert.Contains("VectorStore:Provider 'qdrant'", error.Message);
        Assert.Contains("\"inmemory\" or \"postgres\"", error.Message);
    }

    [Fact]
    public void AddVectorStore_postgres_without_a_connection_string_fails_on_resolve_and_names_the_key()
    {
        // Registration needs no database; only asking for the store reads the connection string
        using ServiceProvider services = BuildServices(new() { ["VectorStore:Provider"] = "postgres" });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<ITextVectorStore>());
        Assert.Contains("ConnectionStrings:Vectors", error.Message);
        Assert.Contains("dotnet user-secrets set", error.Message);
    }

    // Runs only with AILAB_PG set and `docker compose up -d`, so `dotnet test` stays offline.
    // Uses its own table (chunks_test), dropped at the start and left afterwards so it can be inspected with \d.
    [Fact]
    public async Task Postgres_creates_an_hnsw_index_and_returns_closest_first()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AILAB_PG") is null, "Set AILAB_PG=1 with Docker running to run the Postgres test.");
        string connectionString = Environment.GetEnvironmentVariable("AILAB_PG_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=ailab;Username=ailab;Password=ailab";

        using ServiceProvider services = BuildServices(new()
        {
            ["VectorStore:Provider"] = "postgres",
            ["VectorStore:Collection"] = "chunks_test",
            ["ConnectionStrings:Vectors"] = connectionString,
        });
        await services.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>().EnsureCollectionDeletedAsync(Ct);
        ITextVectorStore store = services.GetRequiredService<ITextVectorStore>();

        // Postgres alone only says "Call to vector store failed."
        InvalidOperationException missing = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SearchAsync(Vector(1, 0), top: 3, Ct));
        Assert.Contains("'chunks_test' does not exist", missing.Message);

        await store.EnsureCreatedAsync(Ct);
        await UpsertThree(store);
        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Vector(1, 0), top: 3, Ct);

        Assert.Equal(["same", "halfway", "orthogonal"], hits.Select(h => h.Key));
        Assert.Equal(0, hits[0].Score, 4);
        Assert.Equal(1 - Math.Sqrt(0.5), hits[1].Score, 4);
        Assert.Equal(1, hits[2].Score, 4);

        // Postgres returns NaN for a zero query vector; the store must refuse it rather than pass it on
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SearchAsync(Vector(0, 0), top: 3, Ct));

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlCommand command = dataSource.CreateCommand("SELECT indexdef FROM pg_indexes WHERE tablename = 'chunks_test'");
        var indexes = new List<string>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(Ct))
            while (await reader.ReadAsync(Ct))
                indexes.Add(reader.GetString(0));

        Assert.Contains(indexes, i => i.Contains("USING hnsw", StringComparison.OrdinalIgnoreCase));
    }

    // Inserted out of order, so the test sees the store sort them
    private static Task UpsertThree(ITextVectorStore store) => store.UpsertAsync(
        [
            new TextChunk("orthogonal", "points along e1", "doc.md"),
            new TextChunk("same", "points along e0", "doc.md"),
            new TextChunk("halfway", "halfway between e0 and e1", "doc.md"),
        ],
        [Vector(0, 1), Vector(1, 0), Vector(Math.Sqrt(0.5), Math.Sqrt(0.5))],
        Ct);

    private static ReadOnlyMemory<float> Vector(double x, double y)
    {
        var values = new float[TextChunkRecord.Dimensions];
        values[0] = (float)x;
        values[1] = (float)y;
        return values;
    }

    private static async Task<ITextVectorStore> InMemoryStore()
    {
        VectorStoreCollection<string, TextChunkRecord> collection = new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test");
        var store = new TextVectorStore(collection, NullLogger<TextVectorStore>.Instance);
        await store.EnsureCreatedAsync(Ct);
        return store;
    }

    // ValidateOnBuild, as in Development, so a registration ASP.NET Core would refuse at startup fails here too
    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton(config)
            .AddLogging()
            .AddVectorStore(config)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
