using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.VectorStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace AiLab.Api.Tests;

// Keyword search (IKeywordSearch) next to vector search: offline over the in-memory stand-in, and over Postgres with AILAB_PG=1.
// Vectors as in TextVectorStoreTests: 768 long, only the first two axes used.
public class KeywordSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Question = "Which environment variable sets the JWT secret?";

    // Has environment, variable, JWT and secret, but not "sets": AND finds nothing, OR finds it
    private const string JwtText = "Configuration: the environment variable Jwt__Secret holds the JWT signing secret.";
    private const string RotationText = "Rotate the JWT secret in every environment.";
    private const string UnrelatedText = "Password reset links expire after one hour.";

    [Fact]
    public async Task InMemory_ranks_a_chunk_with_more_query_words_higher()
    {
        using ServiceProvider services = BuildServices(InMemory);
        await SeedAsync(services);

        IReadOnlyList<SearchHit> hits = await services.GetRequiredService<IKeywordSearch>().SearchAsync(Question, Vector(1, 0), top: 5, ct: Ct);

        // JwtText has 4 query words, RotationText 3 (jwt, secret, environment), UnrelatedText none
        Assert.Equal([JwtText, RotationText], hits.Select(h => h.Chunk.Text));
    }

    [Fact]
    public async Task InMemory_respects_the_chunker_filter_and_top()
    {
        using ServiceProvider services = BuildServices(InMemory);
        await SeedAsync(services);
        IKeywordSearch keywords = services.GetRequiredService<IKeywordSearch>();

        Assert.Equal(["fixed"], (await keywords.SearchAsync(Question, Vector(1, 0), top: 5, chunker: "fixed", ct: Ct)).Select(h => h.Chunk.Chunker));
        Assert.Single(await keywords.SearchAsync(Question, Vector(1, 0), top: 1, ct: Ct));
    }

    [Fact]
    public async Task InMemory_hits_carry_the_cosine_distance()
    {
        using ServiceProvider services = BuildServices(InMemory);
        await SeedAsync(services);

        IReadOnlyList<SearchHit> hits = await services.GetRequiredService<IKeywordSearch>().SearchAsync(Question, Vector(1, 0), top: 5, ct: Ct);

        Assert.Equal(0, hits[0].Score, 4);                  // JwtText is stored as e0
        Assert.Equal(1 - Math.Sqrt(0.5), hits[1].Score, 4); // RotationText halfway
    }

    [Fact]
    public async Task InMemory_returns_nothing_for_stopwords_or_unknown_words()
    {
        using ServiceProvider services = BuildServices(InMemory);
        await SeedAsync(services);
        IKeywordSearch keywords = services.GetRequiredService<IKeywordSearch>();

        Assert.Empty(await keywords.SearchAsync("what is the", Vector(1, 0), top: 5, ct: Ct));
        Assert.Empty(await keywords.SearchAsync("kubernetes helm", Vector(1, 0), top: 5, ct: Ct));
    }

    [Fact]
    public void Both_providers_register_keyword_search()
    {
        using ServiceProvider inMemory = BuildServices(InMemory);
        Assert.IsType<InMemoryKeywordSearch>(inMemory.GetRequiredService<IKeywordSearch>());

        // Resolving doesn't connect: the data source opens a connection on the first command
        using ServiceProvider postgres = BuildServices(Postgres("keyword_test", "Host=localhost;Port=1;Database=x;Username=x;Password=x"));
        Assert.IsType<PostgresKeywordSearch>(postgres.GetRequiredService<IKeywordSearch>());
    }

    [Theory]
    [InlineData("corpus; DROP TABLE corpus")]
    [InlineData("corpus\"")]
    public void A_collection_name_that_is_not_a_plain_identifier_is_refused(string table)
    {
        using ServiceProvider services = BuildServices(Postgres(table, "Host=localhost;Port=1;Database=x;Username=x;Password=x"));

        Assert.Throws<ArgumentException>(() => services.GetRequiredService<IKeywordSearch>());
    }

    // Runs only with AILAB_PG set and `docker compose up -d`. Its own table (keyword_test), dropped at the start, left afterwards.
    [Fact]
    public async Task Postgres_keyword_search_ors_the_words_and_carries_the_distance()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AILAB_PG") is null, "Set AILAB_PG=1 with Docker running to run the Postgres test.");
        string connectionString = Environment.GetEnvironmentVariable("AILAB_PG_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=ailab;Username=ailab;Password=ailab";

        using ServiceProvider services = BuildServices(Postgres("keyword_test", connectionString));
        await services.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>().EnsureCollectionDeletedAsync(Ct);
        IKeywordSearch keywords = services.GetRequiredService<IKeywordSearch>();

        // Before ingest: the same clear error as vector search, not a raw "relation does not exist"
        await Assert.ThrowsAsync<VectorCollectionMissingException>(() => keywords.SearchAsync(Question, Vector(1, 0), top: 5, ct: Ct));

        await SeedAsync(services);
        await keywords.EnsureIndexAsync(Ct);

        // The GIN index exists, on the same expression the query filters on
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        string? indexDef = (string?)await dataSource
            .CreateCommand($"SELECT indexdef FROM pg_indexes WHERE tablename = 'keyword_test' AND indexname = '{PostgresKeywordSearch.IndexName("keyword_test")}'")
            .ExecuteScalarAsync(Ct);
        Assert.NotNull(indexDef);
        Assert.Contains("USING gin", indexDef, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("to_tsvector('english'", indexDef);

        // AND (plainto_tsquery) finds nothing: no chunk has "sets"
        long andCount = (long)(await dataSource
            .CreateCommand($"""SELECT count(*) FROM keyword_test WHERE to_tsvector('english', "Text") @@ plainto_tsquery('english', '{Question}')""")
            .ExecuteScalarAsync(Ct))!;
        Assert.Equal(0, andCount);

        // OR finds the JWT chunk first, with its cosine distance to the query vector
        IReadOnlyList<SearchHit> all = await keywords.SearchAsync(Question, Vector(1, 0), top: 5, ct: Ct);
        Assert.Equal([JwtText, RotationText], all.Select(h => h.Chunk.Text));
        Assert.Equal(0, all[0].Score, 4);
        Assert.Equal(1 - Math.Sqrt(0.5), all[1].Score, 4);
        Assert.Equal(Chunk("heading", "env.md", 1, JwtText, "Configuration"), all[0].Chunk);

        // The chunker filter becomes a WHERE; top limits
        Assert.Equal(["fixed"], (await keywords.SearchAsync(Question, Vector(1, 0), top: 5, chunker: "fixed", ct: Ct)).Select(h => h.Chunk.Chunker));
        Assert.Equal([JwtText], (await keywords.SearchAsync(Question, Vector(1, 0), top: 1, ct: Ct)).Select(h => h.Chunk.Text));

        // A query of stopwords only becomes an empty tsquery: no hits, no error
        Assert.Empty(await keywords.SearchAsync("what is the", Vector(1, 0), top: 5, ct: Ct));
    }

    private static Dictionary<string, string?> InMemory => new() { ["VectorStore:Provider"] = "inmemory" };

    private static Dictionary<string, string?> Postgres(string table, string connectionString) => new()
    {
        ["VectorStore:Provider"] = "postgres",
        ["VectorStore:Collection"] = table,
        ["ConnectionStrings:Vectors"] = connectionString,
    };

    private static async Task SeedAsync(ServiceProvider services)
    {
        ITextVectorStore store = services.GetRequiredService<ITextVectorStore>();
        await store.EnsureCreatedAsync(Ct);
        await store.UpsertAsync(
            [Chunk("heading", "env.md", 1, JwtText, "Configuration"), Chunk("fixed", "ops.md", 1, RotationText), Chunk("heading", "auth.md", 1, UnrelatedText)],
            [Vector(1, 0), Vector(Math.Sqrt(0.5), Math.Sqrt(0.5)), Vector(0, 1)],
            Ct);
    }

    private static TextChunk Chunk(string chunker, string source, int position, string text, string? heading = null) =>
        new(chunker, source, position, text, heading, StartLine: position * 10, EndLine: position * 10 + 4);

    private static ReadOnlyMemory<float> Vector(double x, double y)
    {
        var values = new float[TextChunkRecord.Dimensions];
        values[0] = (float)x;
        values[1] = (float)y;
        return values;
    }

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
