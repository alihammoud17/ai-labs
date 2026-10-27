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
        await service.IndexAsync([Chunk("c1", "y axis"), Chunk("c2", "x axis"), Chunk("c3", "both")], Ct);

        IReadOnlyList<SearchHit> hits = await service.SearchAsync("x query", top: 3, Ct);

        Assert.Equal(["c2", "c3", "c1"], hits.Select(h => h.Key));
        Assert.Equal(0, hits[0].Score, 4);
    }

    [Fact]
    public async Task Index_embeds_in_batches_and_stores_every_chunk()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();
        TextChunk[] chunks = Enumerable.Range(1, 70).Select(i => Chunk($"c{i}", $"x text {i}")).ToArray();

        int stored = await service.IndexAsync(chunks, Ct);

        Assert.Equal(70, stored);
        Assert.Equal([VectorSearchService.EmbeddingBatchSize, VectorSearchService.EmbeddingBatchSize, 6], generator.BatchSizes);
        Assert.Equal(20, (await service.SearchAsync("x query", top: 20, Ct)).Count);
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
            () => service.IndexAsync([Chunk("ok", "x text"), Chunk("blank", text)], Ct));

        Assert.Contains("'blank'", error.Message);
        Assert.Empty(generator.BatchSizes);
    }

    [Fact]
    public async Task Duplicate_keys_are_refused_and_named()
    {
        (VectorSearchService service, MappedEmbeddingGenerator generator) = Create();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.IndexAsync([Chunk("same", "x one"), Chunk("other", "y"), Chunk("same", "x two")], Ct));

        Assert.Contains("same", error.Message);
        Assert.DoesNotContain("other", error.Message);
        Assert.Empty(generator.BatchSizes);
    }

    [Fact]
    public async Task A_wrong_number_of_embeddings_is_refused()
    {
        var store = new TextVectorStore(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test"), NullLogger<TextVectorStore>.Instance);
        var service = new VectorSearchService(store, new FixedEmbeddingGenerator([Vector(1, 0)]), NullLogger<VectorSearchService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IndexAsync([Chunk("a", "x"), Chunk("b", "y")], Ct));
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

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SearchAsync("x query", top, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_query_is_refused(string query)
    {
        (VectorSearchService service, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query, ct: Ct));
    }

    private static (VectorSearchService, MappedEmbeddingGenerator) Create()
    {
        var generator = new MappedEmbeddingGenerator(text => text.StartsWith('x') ? Vector(1, 0)
            : text.StartsWith('y') ? Vector(0, 1)
            : Vector(Math.Sqrt(0.5), Math.Sqrt(0.5)));
        var store = new TextVectorStore(new InMemoryVectorStore().GetCollection<string, TextChunkRecord>("test"), NullLogger<TextVectorStore>.Instance);
        return (new VectorSearchService(store, generator, NullLogger<VectorSearchService>.Instance), generator);
    }

    private static TextChunk Chunk(string key, string text) => new(key, text, "doc.md");

    private static float[] Vector(double x, double y)
    {
        var values = new float[TextChunkRecord.Dimensions];
        values[0] = (float)x;
        values[1] = (float)y;
        return values;
    }
}
