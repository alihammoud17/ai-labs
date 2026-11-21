using System.Net;
using System.Text;
using System.Text.Json;
using AiLab.Application.Services;
using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.VectorStore;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiLab.Api.Tests;

// Week 7 (Block 5): Bedrock embeddings without an account or the emulator (which serves no embedding inference).
// Everything real except the HTTP answer: the AWS SDK client (endpoint, SigV4 signing, serialization, response parsing)
// and AWS.Bedrock.MEAI's generator, with fake credentials and an HttpClientFactory whose handler answers like
// Titan Text Embeddings v2: {"embedding":[...],"inputTextTokenCount":N}.
public sealed class BedrockEmbeddingTests
{
    private const string Titan = "amazon.titan-embed-text-v2:0";
    private const int Size = 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_signed_InvokeModel_per_text_with_the_size_in_the_body_and_the_answer_read_back()
    {
        var titan = new FakeTitan(Size);
        using IEmbeddingGenerator<string, Embedding<float>> generator = Generator(titan);

        GeneratedEmbeddings<Embedding<float>> result = await generator.GenerateAsync(["a", "b"], cancellationToken: Ct);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(Size, e.Vector.Length));
        Assert.Equal(6, result.Usage?.InputTokenCount); // 3 per text, added up by the generator

        // No batching: one request per text, in order, each with the size
        Assert.Equal(2, titan.Requests.Count);
        Assert.Equal(["a", "b"], titan.Requests.Select(r => r.InputText));
        Assert.All(titan.Requests, r =>
        {
            Assert.Equal(Size, r.Dimensions);
            Assert.False(r.SentNormalize); // never sent: Titan's default (true) applies, which suits cosine distance
            Assert.EndsWith("/invoke", r.Path);
            Assert.Contains("titan-embed-text-v2", r.Path);
            Assert.Equal("AWS4-HMAC-SHA256", r.AuthorizationScheme); // the SDK signed it
        });
    }

    [Fact]
    public void The_metadata_shows_the_provider_model_and_size()
    {
        using IEmbeddingGenerator<string, Embedding<float>> generator = Generator(new FakeTitan(Size));

        EmbeddingGeneratorMetadata? metadata = generator.GetService<EmbeddingGeneratorMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("aws.bedrock", metadata.ProviderName);
        Assert.Equal(Titan, metadata.DefaultModelId);
        Assert.Equal(Size, metadata.DefaultModelDimensions);
    }

    // The embedding half of D1 end to end: Titan-shaped vectors through VectorSearchService into a store sized from
    // configuration (1024), then a search finds the matching chunk. The fake gives each text a vector that points along
    // the axis of its length, so equal lengths are identical (distance 0) and other lengths orthogonal (distance 1).
    [Fact]
    public async Task Titan_vectors_index_and_search_in_a_1024_collection()
    {
        var settings = new VectorStoreSettings("corpus_titan_v2_1024", Size);
        var collection = new InMemoryVectorStore().GetCollection<string, TextChunkRecord>(settings.Collection, TextChunkRecord.Definition(Size));
        var store = new TextVectorStore(collection, settings, NullLogger<TextVectorStore>.Instance);
        using IEmbeddingGenerator<string, Embedding<float>> generator = Generator(new FakeTitan(Size));
        var search = new VectorSearchService(store, generator, NullLogger<VectorSearchService>.Instance);

        IndexResult indexed = await search.IndexAsync(
        [
            new TextChunk("heading", "doc.md", 1, "refunds", null, 1, 1),
            new TextChunk("heading", "doc.md", 2, "webhook retries", null, 2, 2),
        ], Ct);
        IReadOnlyList<SearchHit> hits = await search.SearchAsync("billing", top: 2, ct: Ct); // 7 letters, like "refunds"

        Assert.Equal(2, indexed.Indexed);
        Assert.Equal(6, indexed.InputTokens);
        Assert.Equal("refunds", hits[0].Chunk.Text);
        Assert.Equal(0, hits[0].Score, 4);
        Assert.Equal(1, hits[1].Score, 4);
    }

    // A Titan answer of the wrong size (dimensions mixed up between the generator and the store) is refused by the store
    [Fact]
    public async Task A_768_answer_is_refused_by_a_1024_store_before_anything_is_written()
    {
        var settings = new VectorStoreSettings("corpus_titan_v2_1024", Size);
        var collection = new InMemoryVectorStore().GetCollection<string, TextChunkRecord>(settings.Collection, TextChunkRecord.Definition(Size));
        var store = new TextVectorStore(collection, settings, NullLogger<TextVectorStore>.Instance);
        using IEmbeddingGenerator<string, Embedding<float>> generator = Generator(new FakeTitan(768));
        var search = new VectorSearchService(store, generator, NullLogger<VectorSearchService>.Instance);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => search.IndexAsync([new TextChunk("heading", "doc.md", 1, "refunds", null, 1, 1)], Ct));

        Assert.Contains("768 dimensions", error.Message);
        Assert.Contains("Embedding:Dimensions", error.Message);
        Assert.Empty(await store.GetChunksAsync(ct: Ct));
    }

    // The client the factory would build, but with fake credentials and the fake HTTP answer. No retries, so a mistake fails at once.
    private static IEmbeddingGenerator<string, Embedding<float>> Generator(FakeTitan titan)
    {
        var config = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            HttpClientFactory = new FakeHttpClientFactory(titan),
            MaxErrorRetry = 0,
        };
        return new AmazonBedrockRuntimeClient(new BasicAWSCredentials("test", "test"), config).AsIEmbeddingGenerator(Titan, Size);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    private sealed record TitanRequest(string Path, string? InputText, int? Dimensions, bool SentNormalize, string? AuthorizationScheme);

    // Answers every InvokeModel like Titan v2: a vector of `size` numbers pointing along axis (text length mod size), 3 tokens
    private sealed class FakeTitan(int size) : HttpMessageHandler
    {
        // The SDK adds the header without validation, so the typed Headers.Authorization stays null: read the raw value
        private static string? AuthorizationScheme(HttpRequestMessage request) =>
            request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values) ? values.First().Split(' ')[0] : null;

        private readonly object _lock = new();
        public List<TitanRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument json = JsonDocument.Parse(body);
            JsonElement root = json.RootElement;
            string? text = root.TryGetProperty("inputText", out JsonElement t) ? t.GetString() : null;
            int? dimensions = root.TryGetProperty("dimensions", out JsonElement d) ? d.GetInt32() : null;
            lock (_lock)
                Requests.Add(new TitanRequest(request.RequestUri!.AbsolutePath, text, dimensions, root.TryGetProperty("normalize", out _),
                    AuthorizationScheme(request)));

            var vector = new float[size];
            vector[(text?.Length ?? 0) % size] = 1;
            string answer = JsonSerializer.Serialize(new { embedding = vector, inputTextTokenCount = 3 });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
