using AiLab.Infrastructure.Providers;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;

namespace AiLab.Api.Tests;

// What counts as "the provider failed" (→ 502 provider_unavailable in /ask and /analyze-ticket) and what stays a 500.
// The Bedrock exceptions are the ones seen on aws-emulator (2026-11-20).
public class ProviderErrorClassifierTests
{
    private readonly ProviderErrorClassifier _classifier = new();

    public static TheoryData<Exception> ProviderFailures => new()
    {
        new HttpRequestException("Connection refused (127.0.0.1:9297)"),                // emulator or Ollama down
        new ValidationException("The provided model identifier is invalid."),           // Bedrock: unknown model, 400
        new BedrockRuntimeEventStreamException("Error.", new TaskCanceledException()),  // Bedrock: error inside ConverseStream
        new TimeoutException("A task was canceled."),                                   // AWS SDK attempt timeout
    };

    public static TheoryData<Exception> OurProblems => new()
    {
        new AmazonClientException("Failed to resolve AWS credentials."), // local config: ours to fix, stays a 500
        new InvalidOperationException("Bedrock:EmulatorCaPath points to a missing file."),
        new ArgumentException("ResponseFormat cannot be used with Tools in Amazon Bedrock."),
    };

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public void Provider_failures_are_recognised(Exception ex) => Assert.True(_classifier.IsProviderFailure(ex));

    [Theory]
    [MemberData(nameof(OurProblems))]
    public void Our_own_problems_are_not_provider_failures(Exception ex) => Assert.False(_classifier.IsProviderFailure(ex));
}
