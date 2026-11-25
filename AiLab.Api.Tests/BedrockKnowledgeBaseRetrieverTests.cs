using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.KnowledgeBase;
using Amazon;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Api.Tests;

// Week 8 Tuesday (Task 2.4): the Knowledge Base adapter without an account or the emulator. Everything real except the
// HTTP answer, as in BedrockEmbeddingTests: the AWS SDK client (endpoint, SigV4 signing, serialization, response parsing)
// with fake credentials and an HttpClientFactory whose handler answers like Bedrock's Retrieve.
public sealed class BedrockKnowledgeBaseRetrieverTests
{
    private const string KbId = "KBTEST1234";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retrieve_sends_the_query_and_top_signed_and_maps_uris_to_corpus_paths_in_rank_order()
    {
        var kb = new FakeKnowledgeBase(
            ("s3://ai-lab-docs/README.md", "# Multi-Tenant SaaS API A production-oriented…", 0.42),
            ("s3://ai-lab-docs/docs/contracts.md", "# Service Contracts ## Contract principles…", 0.17));
        var retriever = new BedrockKnowledgeBaseRetriever(Client(kb), new KnowledgeBaseSettings(KbId));

        IReadOnlyList<KnowledgeBasePassage> passages = await retriever.RetrieveAsync("tenant registration", 10, Ct);

        Assert.Equal(
            [new KnowledgeBasePassage("README.md", "# Multi-Tenant SaaS API A production-oriented…", 0.42, 1),
             new KnowledgeBasePassage("docs/contracts.md", "# Service Contracts ## Contract principles…", 0.17, 2)],
            passages);

        var request = Assert.Single(kb.Requests);
        Assert.Equal($"/knowledgebases/{KbId}/retrieve", request.Path);
        Assert.Equal("POST", request.Method);
        Assert.Equal("AWS4-HMAC-SHA256", request.AuthorizationScheme); // the SDK signed it
        Assert.Equal("tenant registration", request.Body["retrievalQuery"]?["text"]?.GetValue<string>());
        var search = request.Body["retrievalConfiguration"]?["vectorSearchConfiguration"];
        Assert.Equal(10, search?["numberOfResults"]?.GetValue<int>());
        Assert.Null(search?["overrideSearchType"]); // unset: Bedrock decides
    }

    [Fact]
    public async Task A_search_type_setting_is_sent_as_overrideSearchType()
    {
        var kb = new FakeKnowledgeBase();
        var retriever = new BedrockKnowledgeBaseRetriever(Client(kb), new KnowledgeBaseSettings(KbId, SearchType: "SEMANTIC"));

        IReadOnlyList<KnowledgeBasePassage> passages = await retriever.RetrieveAsync("q", 5, Ct);

        Assert.Empty(passages);
        var search = Assert.Single(kb.Requests).Body["retrievalConfiguration"]?["vectorSearchConfiguration"];
        Assert.Equal("SEMANTIC", search?["overrideSearchType"]?.GetValue<string>());
        Assert.Equal(5, search?["numberOfResults"]?.GetValue<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Top_outside_retrieves_range_is_refused_before_any_call(int top)
    {
        var kb = new FakeKnowledgeBase();
        var retriever = new BedrockKnowledgeBaseRetriever(Client(kb), new KnowledgeBaseSettings(KbId));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retriever.RetrieveAsync("q", top, Ct));
        Assert.Empty(kb.Requests);
    }

    [Theory]
    [InlineData("s3://ai-lab-docs/README.md", "README.md")]
    [InlineData("s3://ai-lab-docs/docs/contracts.md", "docs/contracts.md")]
    [InlineData("s3://other-bucket/docs/a%20b.md", "docs/a b.md")]
    public void CorpusPath_is_the_key_under_the_bucket(string uri, string expected) =>
        Assert.Equal(expected, BedrockKnowledgeBaseRetriever.CorpusPath(new RetrievalResultLocation { S3Location = new RetrievalResultS3Location { Uri = uri } }));

    [Fact]
    public void A_result_without_an_S3_location_fails_rather_than_inventing_a_source()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BedrockKnowledgeBaseRetriever.CorpusPath(
            new RetrievalResultLocation { Type = RetrievalResultLocationType.WEB }));
        Assert.Contains("S3", error.Message);
    }

    [Fact]
    public void AddKnowledgeBase_registers_nothing_without_KnowledgeBase_Id()
    {
        var services = new ServiceCollection().AddKnowledgeBase(Config([]));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IKnowledgeBaseRetriever));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAmazonBedrockAgentRuntime));
    }

    [Fact]
    public void AddKnowledgeBase_with_an_id_resolves_the_bedrock_retriever_without_calling_aws()
    {
        using var provider = new ServiceCollection()
            .AddKnowledgeBase(Config(new() { ["KnowledgeBase:Id"] = KbId, ["Bedrock:Region"] = "us-west-2" }))
            .BuildServiceProvider();

        Assert.IsType<BedrockKnowledgeBaseRetriever>(provider.GetRequiredService<IKnowledgeBaseRetriever>());
        Assert.Equal(new KnowledgeBaseSettings(KbId), provider.GetRequiredService<KnowledgeBaseSettings>());
        var client = Assert.IsType<AmazonBedrockAgentRuntimeClient>(provider.GetRequiredService<IAmazonBedrockAgentRuntime>());
        Assert.Equal(RegionEndpoint.USWest2, client.Config.RegionEndpoint);
    }

    [Fact]
    public void AddKnowledgeBase_refuses_an_unknown_search_type_at_registration()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddKnowledgeBase(Config(new() { ["KnowledgeBase:Id"] = KbId, ["KnowledgeBase:SearchType"] = "keyword" })));
        Assert.Contains("KnowledgeBase:SearchType", error.Message);
    }

    [Fact]
    public void The_emulator_CA_is_refused_without_a_service_url_for_the_knowledge_base_client_too()
    {
        using var provider = new ServiceCollection()
            .AddKnowledgeBase(Config(new() { ["KnowledgeBase:Id"] = KbId, ["Bedrock:EmulatorCaPath"] = "/tmp/ca.pem" }))
            .BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IAmazonBedrockAgentRuntime>());
        Assert.Contains("Bedrock:ServiceUrl", error.Message);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // The client the factory would build, but with fake credentials and the fake HTTP answer. No retries, so a mistake fails at once.
    internal static AmazonBedrockAgentRuntimeClient Client(HttpMessageHandler handler) =>
        new(new BasicAWSCredentials("test", "test"), new AmazonBedrockAgentRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            HttpClientFactory = new FakeHttpClientFactory(handler),
            MaxErrorRetry = 0,
        });

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    internal sealed record SentRequest(string Method, string Path, JsonObject Body, string? AuthorizationScheme);

    // Answers every Retrieve with the given results, in order, shaped like the service's JSON
    private sealed class FakeKnowledgeBase(params (string Uri, string Text, double Score)[] results) : HttpMessageHandler
    {
        private readonly object _lock = new();
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            string? scheme = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values) ? values.First().Split(' ')[0] : null;
            lock (_lock)
                Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.AbsolutePath, JsonNode.Parse(body)!.AsObject(), scheme));

            var answer = new
            {
                retrievalResults = results.Select(r => new
                {
                    content = new { type = "TEXT", text = r.Text },
                    location = new { type = "S3", s3Location = new { uri = r.Uri } },
                    score = r.Score,
                    metadata = new Dictionary<string, string> { ["x-amz-bedrock-kb-source-uri"] = r.Uri },
                }),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json"),
            };
        }
    }
}
