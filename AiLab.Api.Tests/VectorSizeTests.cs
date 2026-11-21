using AiLab.Domain.DTOs;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.VectorStore;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace AiLab.Api.Tests;

// Week 7 (Block 5): the vector size from Embedding:Dimensions (Block 3, R7). The in-memory connector never checks sizes,
// so the offline tests are about our own checks; only the Postgres test (AILAB_PG=1) can show the schema itself.
public sealed class VectorSizeTests
{
    private const int Titan = 1024;
    private static readonly VectorStoreSettings Bedrock = new("corpus_titan_v2_1024", Titan);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_1024_store_accepts_1024_vectors_and_refuses_768_naming_the_settings()
    {
        ITextVectorStore store = await InMemoryStore(Bedrock);

        await store.UpsertAsync([Chunk(1)], [Axis(Titan, 0)], Ct);
        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Axis(Titan, 0), top: 1, ct: Ct);
        Assert.Equal(0, Assert.Single(hits).Score, 4);

        ArgumentException upsert = await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync([Chunk(2)], [Axis(768, 0)], Ct));
        ArgumentException search = await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(Axis(768, 0), top: 1, ct: Ct));
        foreach (ArgumentException error in new[] { upsert, search })
        {
            Assert.Contains("768 dimensions", error.Message);
            Assert.Contains("'corpus_titan_v2_1024' expects 1024", error.Message);
            Assert.Contains("Embedding:Dimensions", error.Message);
            Assert.Contains("VectorStore:Collection", error.Message);
        }
    }

    // The default is unchanged: a 768 store refuses 1024 (what a Titan generator would send to the old collection)
    [Fact]
    public async Task The_default_store_refuses_1024_vectors()
    {
        ITextVectorStore store = await InMemoryStore(VectorStoreSettings.Default);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync([Chunk(1)], [Axis(Titan, 0)], Ct));

        Assert.Contains("'corpus' expects 768", error.Message);
    }

    [Fact]
    public async Task The_in_memory_keyword_search_refuses_a_query_of_the_wrong_size()
    {
        var keywords = new InMemoryKeywordSearch(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("k", TextChunkRecord.Definition(Titan)), Bedrock);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => keywords.SearchAsync("refunds", Axis(768, 0), top: 5, ct: Ct));

        Assert.Contains("'corpus_titan_v2_1024' expects 1024", error.Message);
    }

    // Checked before any SQL, so no database is needed: the data source never opens a connection
    [Fact]
    public async Task The_Postgres_keyword_search_refuses_a_query_of_the_wrong_size_before_any_sql()
    {
        await using NpgsqlDataSource unused = NpgsqlDataSource.Create("Host=localhost;Port=1;Database=x;Username=x;Password=x");
        await using var keywords = new PostgresKeywordSearch(unused, Bedrock, NullLogger<PostgresKeywordSearch>.Instance);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => keywords.SearchAsync("refunds", Axis(768, 0), top: 5, ct: Ct));

        Assert.Contains("'corpus_titan_v2_1024' expects 1024", error.Message);
    }

    [Fact]
    public void AddVectorStore_reads_the_size_and_the_collection()
    {
        using ServiceProvider services = BuildServices(new()
        {
            ["VectorStore:Provider"] = "inmemory",
            ["VectorStore:Collection"] = "corpus_titan_v2_1024",
            ["Embedding:Dimensions"] = "1024",
        });

        Assert.Equal(Bedrock, services.GetRequiredService<VectorStoreSettings>());
        Assert.Equal("corpus_titan_v2_1024", services.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>().Name);
    }

    [Fact]
    public void AddVectorStore_without_the_settings_keeps_corpus_at_768()
    {
        using ServiceProvider services = BuildServices(new() { ["VectorStore:Provider"] = "inmemory" });

        Assert.Equal(VectorStoreSettings.Default, services.GetRequiredService<VectorStoreSettings>());
    }

    // At registration, so the host stops at startup instead of failing on the first request
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void AddVectorStore_refuses_a_size_that_is_not_positive(string dimensions)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BuildServices(new() { ["VectorStore:Provider"] = "inmemory", ["Embedding:Dimensions"] = dimensions }));

        Assert.Contains("Embedding:Dimensions must be positive", error.Message);
    }

    [Fact]
    public void AddVectorStore_refuses_a_size_that_is_not_a_number()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BuildServices(new() { ["VectorStore:Provider"] = "inmemory", ["Embedding:Dimensions"] = "lots" }));

        Assert.Contains("Embedding:Dimensions", error.Message);
    }

    // R9: the definition really sets the column. Runs only with AILAB_PG set and `docker compose up -d`.
    // Its own table (chunks_test_1024), dropped at the start and left afterwards so it can be inspected with \d.
    [Fact]
    public async Task Postgres_creates_a_vector_1024_column_from_the_configured_size()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AILAB_PG") is null, "Set AILAB_PG=1 with Docker running to run the Postgres test.");
        string connectionString = Environment.GetEnvironmentVariable("AILAB_PG_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=ailab;Username=ailab;Password=ailab";

        using ServiceProvider services = BuildServices(new()
        {
            ["VectorStore:Provider"] = "postgres",
            ["VectorStore:Collection"] = "chunks_test_1024",
            ["Embedding:Dimensions"] = "1024",
            ["ConnectionStrings:Vectors"] = connectionString,
        });
        await services.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>().EnsureCollectionDeletedAsync(Ct);
        ITextVectorStore store = services.GetRequiredService<ITextVectorStore>();

        await store.EnsureCreatedAsync(Ct);
        await store.UpsertAsync([Chunk(1)], [Axis(Titan, 0)], Ct);
        IReadOnlyList<SearchHit> hits = await store.SearchAsync(Axis(Titan, 0), top: 1, ct: Ct);
        Assert.Equal(0, Assert.Single(hits).Score, 4);

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using NpgsqlCommand column = dataSource.CreateCommand(
            """SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = '"chunks_test_1024"'::regclass AND attname = 'Embedding'""");
        Assert.Equal("vector(1024)", (string?)await column.ExecuteScalarAsync(Ct));

        // Cosine distance and HNSW still come from the record's attribute: the definition only set the size
        await using NpgsqlCommand index = dataSource.CreateCommand("SELECT indexdef FROM pg_indexes WHERE tablename = 'chunks_test_1024'");
        var indexes = new List<string>();
        await using (NpgsqlDataReader reader = await index.ExecuteReaderAsync(Ct))
            while (await reader.ReadAsync(Ct))
                indexes.Add(reader.GetString(0));
        Assert.Contains(indexes, i => i.Contains("USING hnsw", StringComparison.OrdinalIgnoreCase) && i.Contains("vector_cosine_ops"));
    }

    private static TextChunk Chunk(int position) => new("heading", "doc.md", position, $"chunk {position}", null, position, position);

    private static ReadOnlyMemory<float> Axis(int size, int axis)
    {
        var values = new float[size];
        values[axis] = 1;
        return values;
    }

    private static async Task<ITextVectorStore> InMemoryStore(VectorStoreSettings settings)
    {
        var collection = new InMemoryVectorStore().GetCollection<string, TextChunkRecord>(settings.Collection, TextChunkRecord.Definition(settings.Dimensions));
        var store = new TextVectorStore(collection, settings, NullLogger<TextVectorStore>.Instance);
        await store.EnsureCreatedAsync(Ct);
        return store;
    }

    // ValidateOnBuild, as in Development
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
