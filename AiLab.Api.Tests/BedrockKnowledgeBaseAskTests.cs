using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure.KnowledgeBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Api.Tests;

// Week 8 Tuesday (Task 5.1): RetrieveAndGenerate behind IKnowledgeBaseAsk, through the real SDK with fake HTTP, as in
// BedrockKnowledgeBaseRetrieverTests: an answer with 2 citations, mapped to corpus-relative sources.
public sealed class BedrockKnowledgeBaseAskTests
{
    private const string KbId = "KBTEST1234";
    private const string ModelArn = "arn:aws:bedrock:us-east-1::foundation-model/amazon.nova-lite-v1:0";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ask_sends_the_question_kb_and_model_and_maps_the_answer_and_both_citations()
    {
        var kb = new FakeRetrieveAndGenerate();
        var ask = new BedrockKnowledgeBaseAsk(BedrockKnowledgeBaseRetrieverTests.Client(kb), new KnowledgeBaseSettings(KbId, ModelArn: ModelArn));

        KnowledgeBaseAnswer answer = await ask.AskAsync("What is the current internal billing contract version?", Ct);

        Assert.Equal("The current version is 2026-03-18. Callbacks are signed.", answer.Text);
        Assert.Equal("session-1", answer.SessionId);
        Assert.Equal(2, answer.Citations.Count);
        Assert.Equal((0, 34), (answer.Citations[0].SpanStart, answer.Citations[0].SpanEnd));
        Assert.Equal([new KnowledgeBaseReference("docs/contracts.md", "Current contract version: `2026-03-18`.")], answer.Citations[0].References);
        Assert.Equal([new KnowledgeBaseReference("README.md", "Signed, versioned, idempotent internal billing callbacks")], answer.Citations[1].References);

        var request = Assert.Single(kb.Requests);
        Assert.Equal("/retrieveAndGenerate", request.Path);
        Assert.Equal("AWS4-HMAC-SHA256", request.AuthorizationScheme);
        Assert.Equal("What is the current internal billing contract version?", request.Body["input"]?["text"]?.GetValue<string>());
        var config = request.Body["retrieveAndGenerateConfiguration"];
        Assert.Equal("KNOWLEDGE_BASE", config?["type"]?.GetValue<string>());
        Assert.Equal(KbId, config?["knowledgeBaseConfiguration"]?["knowledgeBaseId"]?.GetValue<string>());
        Assert.Equal(ModelArn, config?["knowledgeBaseConfiguration"]?["modelArn"]?.GetValue<string>());
        Assert.Null(request.Body["sessionId"]); // always a new session
    }

    [Fact]
    public async Task Without_a_model_arn_it_fails_before_any_call()
    {
        var kb = new FakeRetrieveAndGenerate();
        var ask = new BedrockKnowledgeBaseAsk(BedrockKnowledgeBaseRetrieverTests.Client(kb), new KnowledgeBaseSettings(KbId));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ask.AskAsync("q", Ct));

        Assert.Contains("KnowledgeBase:ModelArn", error.Message);
        Assert.Empty(kb.Requests);
    }

    [Fact]
    public void AddKnowledgeBase_registers_ask_only_with_a_model_arn()
    {
        static IServiceCollection Services(Dictionary<string, string?> values) =>
            new ServiceCollection().AddKnowledgeBase(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        Assert.DoesNotContain(Services(new() { ["KnowledgeBase:Id"] = KbId }), d => d.ServiceType == typeof(IKnowledgeBaseAsk));
        Assert.Contains(Services(new() { ["KnowledgeBase:Id"] = KbId, ["KnowledgeBase:ModelArn"] = ModelArn }), d => d.ServiceType == typeof(IKnowledgeBaseAsk));
    }

    private sealed record SentRequest(string Path, JsonObject Body, string? AuthorizationScheme);

    private sealed class FakeRetrieveAndGenerate : HttpMessageHandler
    {
        private readonly object _lock = new();
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            string? scheme = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values) ? values.First().Split(' ')[0] : null;
            lock (_lock)
                Requests.Add(new SentRequest(request.RequestUri!.AbsolutePath, JsonNode.Parse(body)!.AsObject(), scheme));

            static object Citation(int start, int end, string text, string uri, string passage) => new
            {
                generatedResponsePart = new { textResponsePart = new { text, span = new { start, end } } },
                retrievedReferences = new[]
                {
                    new { content = new { type = "TEXT", text = passage }, location = new { type = "S3", s3Location = new { uri } } },
                },
            };
            var answer = new
            {
                output = new { text = "The current version is 2026-03-18. Callbacks are signed." },
                citations = new[]
                {
                    Citation(0, 34, "The current version is 2026-03-18.", "s3://ai-lab-docs/docs/contracts.md", "Current contract version: `2026-03-18`."),
                    Citation(35, 56, "Callbacks are signed.", "s3://ai-lab-docs/README.md", "Signed, versioned, idempotent internal billing callbacks"),
                },
                sessionId = "session-1",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json"),
            };
        }
    }
}
