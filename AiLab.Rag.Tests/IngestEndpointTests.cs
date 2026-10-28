using System.Net;
using System.Net.Http.Json;
using AiLab.Application.Chunking;
using AiLab.Domain.Responses;
using AiLab.Rag.Tests.Fakes;

namespace AiLab.Rag.Tests;

// Offline, like SearchEndpointTests: the in-memory store and the hashed fake generator.
// The corpus is a temporary folder with one file at the root and one in a subfolder.
public sealed class IngestEndpointTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Guide = """
        # Guide

        Intro to the guide.

        ## Setup

        Install the tools.

        ## Empty

        ## Usage

        Run the app.
        """;

    private const string Notes = "Plain notes without any heading. " + "Just words. ";

    private readonly string _corpus = Path.Combine(Path.GetTempPath(), $"rag-corpus-{Guid.NewGuid():N}");
    private readonly List<RagFactory> _factories = [];

    public IngestEndpointTests()
    {
        Directory.CreateDirectory(Path.Combine(_corpus, "docs"));
        File.WriteAllText(Path.Combine(_corpus, "guide.md"), Guide);
        File.WriteAllText(Path.Combine(_corpus, "docs", "notes.md"), Notes);
        File.WriteAllText(Path.Combine(_corpus, "docs", "ignored.txt"), "Not markdown, so not ingested.");
    }

    public void Dispose()
    {
        foreach (RagFactory factory in _factories)
            factory.Dispose();
        Directory.Delete(_corpus, recursive: true);
    }

    [Fact]
    public async Task Ingest_both_reports_every_file_per_chunker_and_a_total_per_chunker()
    {
        using HttpClient client = Client();

        IngestResponse ingest = await Ingest(client, "both");

        Assert.Equal("both", ingest.Chunker);
        Assert.Equal("inmemory", ingest.Provider);
        // Sources are relative to the corpus with / separators, in ordinal order; the .txt file is skipped
        Assert.Equal([("fixed", "docs/notes.md"), ("fixed", "guide.md"), ("heading", "docs/notes.md"), ("heading", "guide.md")],
            ingest.Files.Select(f => (f.Chunker, f.Source!)));
        // The same counts the chunkers give directly: Guide is one fixed window, and three heading sections (Empty has no body)
        IngestStats headingGuide = ingest.Files.Single(f => f.Chunker == "heading" && f.Source == "guide.md");
        Assert.Equal(new HeadingChunker().Chunk("guide.md", Guide).Count, headingGuide.Chunks);
        Assert.Equal(3, headingGuide.Chunks);

        Assert.Equal(["fixed", "heading"], ingest.Totals.Select(t => t.Chunker));
        Assert.All(ingest.Totals, t =>
        {
            Assert.Null(t.Source);
            Assert.Equal(2, t.Files);
            Assert.Equal(ingest.Files.Where(f => f.Chunker == t.Chunker).Sum(f => f.Chunks), t.Chunks);
            Assert.Equal(ingest.Files.Where(f => f.Chunker == t.Chunker).Sum(f => f.EmbeddingTokens), t.EmbeddingTokens);
            Assert.InRange(t.MinTokens, 1, t.AvgTokens);
            Assert.InRange(t.MaxTokens, t.AvgTokens, 500);
        });
    }

    [Fact]
    public async Task Ingest_one_chunker_stores_only_its_chunks()
    {
        using HttpClient client = Client();

        IngestResponse ingest = await Ingest(client, "heading");

        Assert.All(ingest.Files, f => Assert.Equal("heading", f.Chunker));
        ChunksResponse chunks = await Chunks(client, "");
        Assert.All(chunks.Chunks, c => Assert.Equal("heading", c.Chunker));
        Assert.Equal(ingest.Totals.Single().Chunks, chunks.Count);
    }

    [Fact]
    public async Task Ingesting_twice_replaces_the_rows()
    {
        using HttpClient client = Client();

        await Ingest(client, "both");
        IngestResponse second = await Ingest(client, "both");

        Assert.Equal(second.Totals.Sum(t => t.Chunks), (await Chunks(client, "")).Count);
    }

    [Fact]
    public async Task Chunks_lists_the_stored_metadata_in_reading_order()
    {
        using HttpClient client = Client();
        await Ingest(client, "heading");

        ChunksResponse listed = await Chunks(client, "?chunker=heading&source=guide.md");

        Assert.Equal("heading", listed.Chunker);
        Assert.Equal("guide.md", listed.Source);
        Assert.Equal([1, 2, 3], listed.Chunks.Select(c => c.Position));
        Assert.Equal(["Guide", "Guide > Setup", "Guide > Usage"], listed.Chunks.Select(c => c.Heading));
        ChunkResponse setup = listed.Chunks[1];
        Assert.Equal("heading:guide.md:2", setup.Key);
        Assert.Equal("Guide > Setup\n\nInstall the tools.", setup.Text);
        Assert.Equal((5, 7), (setup.StartLine, setup.EndLine));
        Assert.Equal(IChunker.EstimateTokens(setup.Text), setup.EstimatedTokens);
        Assert.Equal(64, setup.ContentHash.Length);
    }

    [Fact]
    public async Task Search_with_a_chunker_returns_only_its_chunks_with_their_metadata()
    {
        using HttpClient client = Client();
        await Ingest(client, "both");

        HttpResponseMessage response = await client.GetAsync("/search?q=Install%20the%20tools.&top=20&chunker=heading", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchResponse search = (await response.Content.ReadFromJsonAsync<SearchResponse>(Ct))!;
        Assert.Equal("heading", search.Chunker);
        Assert.All(search.Hits, h => Assert.Equal("heading", h.Chunker));
        Assert.Contains(search.Hits, h => h is { Heading: "Guide > Setup", Position: 2, StartLine: 5, EndLine: 7, Source: "guide.md" });
    }

    [Fact]
    public async Task Search_without_a_chunker_returns_all_of_them()
    {
        using HttpClient client = Client();
        await Ingest(client, "both");

        SearchResponse search = (await client.GetFromJsonAsync<SearchResponse>("/search?q=tools&top=20&chunker=", Ct))!;

        Assert.Null(search.Chunker);
        Assert.Equal(["fixed", "heading"], search.Hits.Select(h => h.Chunker).Distinct().Order());
    }

    [Theory]
    [InlineData("/dev/ingest?chunker=lines", "fixed, heading, both")]   // lines is the seed's, not a markdown chunker
    [InlineData("/dev/ingest?chunker=Heading", "fixed, heading, both")] // names are lowercase
    public async Task Ingest_with_an_unknown_chunker_is_a_400(string url, string listed)
    {
        using HttpClient client = Client();

        HttpResponseMessage response = await client.PostAsync(url, null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("invalid_request", error.Code);
        Assert.Contains(listed, error.Error);
    }

    [Theory]
    [InlineData("/search?q=tools&chunker=headings")]
    [InlineData("/dev/chunks?chunker=headings")]
    public async Task An_unknown_chunker_filter_is_a_400_that_lists_the_names(string url)
    {
        using HttpClient client = Client();
        await Ingest(client, "both");

        HttpResponseMessage response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("chunker must be one of: fixed, heading, lines.", error.Error);
    }

    [Fact]
    public async Task A_missing_corpus_folder_is_a_400_with_the_path()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no-such-corpus");
        using HttpClient client = Client(new() { ["Corpus:Path"] = missing });

        HttpResponseMessage response = await client.PostAsync("/dev/ingest", null, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ErrorResponse error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!;
        Assert.Equal("corpus_not_found", error.Code);
        Assert.Contains(missing, error.Error);
    }

    [Fact]
    public async Task Chunks_before_anything_is_stored_is_a_409()
    {
        using HttpClient client = Client();

        HttpResponseMessage response = await client.GetAsync("/dev/chunks", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("collection_missing", (await response.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
    }

    // The default Corpus:Path is the repo's data/corpus, found from the repo root like Seed:Path
    [Fact]
    public async Task The_default_corpus_is_the_repo_snapshot()
    {
        using HttpClient client = Client(new() { ["Corpus:Path"] = "" });

        IngestResponse ingest = await Ingest(client, "heading");

        Assert.Equal(["README.md", "docs/architecture.md", "docs/contracts.md", "docs/decisions.md"], ingest.Files.Select(f => f.Source!));
        Assert.EndsWith(Path.Combine("data", "corpus"), ingest.Corpus);
    }

    [Theory]
    [InlineData("POST", "/dev/ingest")]
    [InlineData("GET", "/dev/chunks")]
    public async Task Dev_endpoints_are_not_mapped_outside_Development(string method, string url)
    {
        using HttpClient client = Client(environment: "Testing");

        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient Client(Dictionary<string, string?>? settings = null, string environment = "Development")
    {
        Dictionary<string, string?> all = new() { ["Corpus:Path"] = _corpus };
        foreach ((string key, string? value) in settings ?? [])
            all[key] = value;
        var factory = new RagFactory(new HashedEmbeddingGenerator(), environment, all);
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static async Task<IngestResponse> Ingest(HttpClient client, string chunker)
    {
        HttpResponseMessage response = await client.PostAsync($"/dev/ingest?chunker={chunker}", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IngestResponse>(Ct))!;
    }

    private static async Task<ChunksResponse> Chunks(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/dev/chunks{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChunksResponse>(Ct))!;
    }
}
