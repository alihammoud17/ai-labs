using AiLab.Api.Tests.Fakes;
using AiLab.Application.Services;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
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
    public async Task Index_embeds_in_batches_and_stores_every_chunk()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();
        TextChunk[] chunks = Enumerable.Range(1, 70).Select(i => Chunk(i, $"x text {i}")).ToArray();

        int stored = await service.IndexAsync(chunks, Ct);

        Assert.Equal(70, stored);
        Assert.Equal([VectorSearchService.EmbeddingBatchSize, VectorSearchService.EmbeddingBatchSize, 6], generator.BatchSizes);
        Assert.Equal(20, (await service.SearchAsync("x query", top: 20, ct: Ct)).Count);
    }

    [Fact]
    public async Task Indexing_nothing_returns_zero_and_makes_no_call()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        Assert.Equal(0, await service.IndexAsync([], Ct));
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

    private static (VectorSearchService, MappedEmbeddingGenerator) Create(bool useTaskPrefixes = false)
    {
        // The prefix is ignored when picking the vector, so prefixed and plain texts land in the same place
        var generator = new MappedEmbeddingGenerator(text =>
            text.Replace(VectorSearchService.DocumentPrefix, "").Replace(VectorSearchService.QueryPrefix, "") is var plain
            && plain.StartsWith('x') ? Vector(1, 0)
            : plain.StartsWith('y') ? Vector(0, 1)
            : Vector(Math.Sqrt(0.5), Math.Sqrt(0.5)));
        var store = new TextVectorStore(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test"), NullLogger<TextVectorStore>.Instance);
        return (new VectorSearchService(store, generator, NullLogger<VectorSearchService>.Instance, useTaskPrefixes), generator);
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
