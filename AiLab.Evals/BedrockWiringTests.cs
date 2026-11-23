using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace AiLab.Evals;

// Offline (no host, no network, no call): a Bedrock run with no judge gets the Bedrock client as the run's chat client,
// built the way EvalFixture builds it, and the judge is never created.
public class BedrockWiringTests
{
    [Fact]
    public void Bedrock_with_judge_none_answers_through_the_bedrock_client_and_never_creates_the_judge()
    {
        var settings = EvalSettings.From(name => name switch
        {
            "AILAB_EVAL_ANSWERS" => "bedrock",
            "AILAB_EVAL_MODEL" => "us.amazon.nova-lite-v1:0",
            "AILAB_EVAL_JUDGE" => "none",
            _ => null,
        });
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bedrock:Region"] = "us-west-2" })
            .Build();
        using IChatClient bedrock = EvalFixture.CreateBedrock(settings.Model!, config);
        bool judgeCreated = false;

        IChatClient? client = EvalReporting.ConfigurationClient(settings, () => { judgeCreated = true; throw new InvalidOperationException("judge"); }, bedrock);

        Assert.Same(bedrock, client);
        var metadata = client!.GetService<ChatClientMetadata>()!;
        Assert.Equal(("aws.bedrock", "us.amazon.nova-lite-v1:0"), (metadata.ProviderName, metadata.DefaultModelId));
        Assert.False(judgeCreated);
        Assert.Equal("bedrock-nova-lite", settings.ExecutionName);
    }
}
