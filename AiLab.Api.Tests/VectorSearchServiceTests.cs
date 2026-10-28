using AiLab.Api.Tests.Fakes;
using AiLab.Application.Services;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.VectorStore;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiLab.Api.Tests;

// Offline: a fake generator turns each text into a vector, over a real in-memory store.
// Texts starting with "x" point along e0, "y" along e1, anything else halfway between.
public class VectorSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_returns_the_indexed_chunks_closest_first()
    {
        (VectorSearchService service, _) = Create();
        await service.IndexAsync([Chunk(1, "y axis"), Chunk(2, "x axis"), Chunk(3, "both")], Ct);

        IReadOnlyList<SearchHit> hits = await service.SearchAsync("x query", top: 3, ct: Ct);

        Assert.Equal(["fixed:doc.md:2", "fixed:doc.md:3", "fixed:doc.md:1"], hits.Select(h => h.Chunk.Key));
        Assert.Equal(0, hits[0].Score, 4);
    }

    [Fact]
    public async Task Search_passes_the_chunker_filter_to_the_store()
    {
        (VectorSearchService service, _) = Create();
        await service.IndexAsync([Chunk(1, "x fixed", chunker: "fixed"), Chunk(1, "y heading", chunker: "heading")], Ct);

        IReadOnlyList<SearchHit> hits = await service.SearchAsync("x query", top: 5, chunker: "heading", ct: Ct);

        Assert.Equal("heading:doc.md:1", Assert.Single(hits).Chunk.Key);
    }

    [Fact]
    public async Task Index_returns_the_provider_tokens_summed_over_the_batches()
    {
        (VectorSearchService service, _) = Create(tokensPerText: 3);

        IndexResult result = await service.IndexAsync(Enumerable.Range(1, 40).Select(i => Chunk(i, $"x text {i}")).ToArray(), Ct);

        Assert.Equal(new IndexResult(40, 120), result); // batches of 32 and 8
    }

    [Fact]
    public async Task Index_returns_null_tokens_when_the_provider_reports_none()
    {
        (VectorSearchService service, _) = Create();

        Assert.Null((await service.IndexAsync([Chunk(1, "x text")], Ct)).InputTokens);
    }

    [Fact]
    public async Task An_unknown_chunker_is_refused_and_the_known_ones_are_listed()
    {
        (VectorSearchService service, _) = Create();

        InvalidRequestException error = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.SearchAsync("x query", chunker: "headings", ct: Ct));

        Assert.Equal("chunker must be one of: fixed, heading, lines.", error.Message);
    }

    [Fact]
    public async Task Index_embeds_in_batches_and_stores_every_chunk()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();
        TextChunk[] chunks = Enumerable.Range(1, 70).Select(i => Chunk(i, $"x text {i}")).ToArray();

        IndexResult stored = await service.IndexAsync(chunks, Ct);

        Assert.Equal(70, stored.Indexed);
        Assert.Equal([VectorSearchService.EmbeddingBatchSize, VectorSearchService.EmbeddingBatchSize, 6], generator.BatchSizes);
        Assert.Equal(20, (await service.SearchAsync("x query", top: 20, ct: Ct)).Count);
    }

    [Fact]
    public async Task Indexing_nothing_returns_zero_and_makes_no_call()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        Assert.Equal(new IndexResult(0, 0), await service.IndexAsync([], Ct));
        Assert.Empty(generator.BatchSizes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_chunk_is_refused_before_anything_is_embedded(string text)
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.IndexAsync([Chunk(1, "x text"), Chunk(2, text)], Ct));

        Assert.Contains("'fixed:doc.md:2'", error.Message);
        Assert.Empty(generator.BatchSizes);
    }

    [Fact]
    public async Task Duplicate_keys_are_refused_and_named()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.IndexAsync([Chunk(1, "x one"), Chunk(2, "y"), Chunk(1, "x two")], Ct));

        Assert.Contains("fixed:doc.md:1", error.Message);
        Assert.DoesNotContain("fixed:doc.md:2", error.Message);
        Assert.Empty(generator.BatchSizes);
    }

    [Fact]
    public async Task A_wrong_number_of_embeddings_is_refused()
    {
        var store = new TextVectorStore(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test"), NullLogger<TextVectorStore>.Instance);
        var service = new VectorSearchService(store, new FixedEmbeddingGenerator([Vector(1, 0)]), NullLogger<VectorSearchService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IndexAsync([Chunk(1, "x"), Chunk(2, "y")], Ct));
    }

    [Fact]
    public async Task Search_before_anything_is_indexed_fails_and_names_the_collection()
    {
        (VectorSearchService service, _) = Create();

        VectorCollectionMissingException error = await Assert.ThrowsAsync<VectorCollectionMissingException>(() => service.SearchAsync("x query", ct: Ct));

        Assert.Contains("'test'", error.Message);
        Assert.Contains("VectorStore:Collection", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Top_outside_1_to_20_is_refused(int top)
    {
        (VectorSearchService service, _) = Create();

        InvalidRequestException error = await Assert.ThrowsAsync<InvalidRequestException>(() => service.SearchAsync("x query", top, ct: Ct));

        Assert.Equal("top must be between 1 and 20.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_query_is_refused(string query)
    {
        (VectorSearchService service, _) = Create();

        await Assert.ThrowsAsync<InvalidRequestException>(() => service.SearchAsync(query, ct: Ct));
    }

    [Fact]
    public async Task Without_task_prefixes_the_texts_are_embedded_as_they_are()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        await service.IndexAsync([Chunk(1, "x axis")], Ct);
        await service.SearchAsync("x query", ct: Ct);

        Assert.Equal(["x axis", "x query"], generator.Inputs);
    }

    [Fact]
    public async Task With_task_prefixes_only_the_embedding_input_is_prefixed()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create(useTaskPrefixes: true);

        await service.IndexAsync([Chunk(1, "x axis"), Chunk(2, "y axis")], Ct);
        IReadOnlyList<SearchHit> hits = await service.SearchAsync("x query", top: 2, ct: Ct);

        Assert.Equal(["search_document: x axis", "search_document: y axis", "search_query: x query"], generator.Inputs);
        // The stored text and the hits stay unprefixed
        Assert.Equal(["x axis", "y axis"], hits.Select(h => h.Chunk.Text));
    }

    [Fact]
    public async Task Sync_embeds_everything_once_and_nothing_on_a_second_run()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator, _) = CreateWithStore(tokensPerText: 3);
        var corpus = Corpus(("a.md", ["x one", "x two"]), ("b.md", ["y one"]));

        IReadOnlyList<SyncResult> first = await service.SyncAsync("fixed", corpus, ct: Ct);
        int callsAfterFirst = generator.BatchSizes.Count;
        IReadOnlyList<SyncResult> second = await service.SyncAsync("fixed", corpus, ct: Ct);

        Assert.Equal([("a.md", 2, 2, 0, 0, 6L), ("b.md", 1, 1, 0, 0, 3L)], first.Select(Counts));
        Assert.Equal([("a.md", 2, 0, 2, 0, 0L), ("b.md", 1, 0, 1, 0, 0L)], second.Select(Counts));
        Assert.Equal(callsAfterFirst, generator.BatchSizes.Count);
    }

    [Fact]
    public async Task Sync_embeds_only_the_chunks_whose_text_changed()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator, ITextVectorStore store) = CreateWithStore();
        await service.SyncAsync("fixed", Corpus(("a.md", ["x one", "x two", "x three"])), ct: Ct);
        generator.Inputs.Clear();

        SyncResult result = Assert.Single(await service.SyncAsync("fixed", Corpus(("a.md", ["x one", "x TWO", "x three"])), ct: Ct));

        Assert.Equal(("a.md", 3, 1, 2, 0, (long?)null), Counts(result));
        Assert.Equal(["x TWO"], generator.Inputs);
        Assert.Equal("x TWO", (await store.GetChunksAsync("fixed", "a.md", Ct))[1].Text);
    }

    [Fact]
    public async Task Sync_deletes_the_positions_a_shorter_source_no_longer_has()
    {
        (VectorSearchService service, _, ITextVectorStore store) = CreateWithStore();
        await service.SyncAsync("fixed", Corpus(("a.md", ["x one", "x two", "x three"])), ct: Ct);

        SyncResult result = Assert.Single(await service.SyncAsync("fixed", Corpus(("a.md", ["x one"])), ct: Ct));

        Assert.Equal(2, result.Deleted);
        Assert.Equal(["fixed:a.md:1"], (await store.GetChunksAsync("fixed", ct: Ct)).Select(c => c.Key));
    }

    [Fact]
    public async Task Sync_deletes_every_chunk_of_a_source_that_left_the_corpus()
    {
        (VectorSearchService service, _, ITextVectorStore store) = CreateWithStore();
        await service.SyncAsync("fixed", Corpus(("a.md", ["x one"]), ("gone.md", ["y one", "y two"])), ct: Ct);

        IReadOnlyList<SyncResult> results = await service.SyncAsync("fixed", Corpus(("a.md", ["x one"])), ct: Ct);

        Assert.Equal([("a.md", 1, 0, 1, 0, 0L), ("gone.md", 0, 0, 0, 2, 0L)], results.Select(Counts));
        Assert.Empty(await store.GetChunksAsync(source: "gone.md", ct: Ct));
    }

    [Fact]
    public async Task Sync_with_force_embeds_everything_again()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator, _) = CreateWithStore();
        var corpus = Corpus(("a.md", ["x one", "x two"]));
        await service.SyncAsync("fixed", corpus, ct: Ct);
        generator.Inputs.Clear();

        SyncResult result = Assert.Single(await service.SyncAsync("fixed", corpus, force: true, ct: Ct));

        Assert.Equal((2, 0), (result.Embedded, result.Unchanged));
        Assert.Equal(["x one", "x two"], generator.Inputs);
    }

    [Fact]
    public async Task Sync_never_touches_another_chunkers_rows()
    {
        (VectorSearchService service, _, ITextVectorStore store) = CreateWithStore();
        await service.SyncAsync("heading", Corpus([("a.md", ["x heading"]), ("b.md", ["y heading"])], "heading"), ct: Ct);

        // fixed sees neither heading row: nothing of its own is stored, so nothing is deleted
        IReadOnlyList<SyncResult> results = await service.SyncAsync("fixed", Corpus(("a.md", ["x fixed"])), ct: Ct);

        Assert.Equal([("a.md", 1, 1, 0, 0, (long?)null)], results.Select(Counts));
        Assert.Equal(2, (await store.GetChunksAsync("heading", ct: Ct)).Count);
    }

    [Fact]
    public async Task Sync_refuses_a_chunk_filed_under_the_wrong_source_before_writing_anything()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator, _) = CreateWithStore();
        var corpus = new Dictionary<string, IReadOnlyList<TextChunk>>
        {
            ["a.md"] = [new TextChunk("fixed", "a.md", 1, "x one", null, 1, 1)],
            ["b.md"] = [new TextChunk("fixed", "a.md", 2, "x two", null, 2, 2)],
        };

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => service.SyncAsync("fixed", corpus, ct: Ct));

        Assert.Contains("'fixed:a.md:2'", error.Message);
        Assert.Empty(generator.BatchSizes);
    }

    // One chunk per text, positions from 1, under the given chunker
    private static Dictionary<string, IReadOnlyList<TextChunk>> Corpus(params (string Source, string[] Texts)[] files) =>
        Corpus(files, "fixed");

    private static Dictionary<string, IReadOnlyList<TextChunk>> Corpus((string Source, string[] Texts)[] files, string chunker) =>
        files.ToDictionary(f => f.Source, f => (IReadOnlyList<TextChunk>)f.Texts
            .Select((text, i) => new TextChunk(chunker, f.Source, i + 1, text, null, i + 1, i + 1)).ToList());

    private static (string, int, int, int, int, long?) Counts(SyncResult r) => (r.Source, r.Chunks, r.Embedded, r.Unchanged, r.Deleted, r.InputTokens);

    private static (VectorSearchService, MappedEmbeddingGenerator) Create(bool useTaskPrefixes = false, int? tokensPerText = null)
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator, _) = CreateWithStore(useTaskPrefixes, tokensPerText);
        return (service, generator);
    }

    private static (VectorSearchService, MappedEmbeddingGenerator, ITextVectorStore) CreateWithStore(bool useTaskPrefixes = false, int? tokensPerText = null)
    {
        // The prefix is ignored when picking the vector, so prefixed and plain texts land in the same place
        var generator = new MappedEmbeddingGenerator(text =>
            text.Replace(VectorSearchService.DocumentPrefix, "").Replace(VectorSearchService.QueryPrefix, "") is var plain
            && plain.StartsWith('x') ? Vector(1, 0)
            : plain.StartsWith('y') ? Vector(0, 1)
            : Vector(Math.Sqrt(0.5), Math.Sqrt(0.5)), tokensPerText);
        var store = new TextVectorStore(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test"), NullLogger<TextVectorStore>.Instance);
        return (new VectorSearchService(store, generator, NullLogger<VectorSearchService>.Instance, useTaskPrefixes), generator, store);
    }

    private static TextChunk Chunk(int position, string text, string chunker = "fixed") =>
        new(chunker, "doc.md", position, text, Heading: null, position, position);

    private static float[] Vector(double x, double y)
    {
        var values = new float[TextChunkRecord.Dimensions];
        values[0] = (float)x;
        values[1] = (float)y;
        return values;
    }
}
