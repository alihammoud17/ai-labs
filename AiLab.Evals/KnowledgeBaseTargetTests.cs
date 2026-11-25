using AiLab.Evals.Hosting;

namespace AiLab.Evals;

// Offline: the label and result names come from the endpoint, so an emulator run can never be labelled Bedrock
public class KnowledgeBaseTargetTests
{
    [Theory]
    [InlineData("https://127.0.0.1:9297", null, null)]
    [InlineData(null, "https://127.0.0.1:9297", null)]          // a sourced ai-lab-kb.env, no Bedrock__ServiceUrl
    [InlineData(null, null, "https://127.0.0.1:9297")]          // the agent runtime's own override
    [InlineData("", "https://127.0.0.1:9297", null)]
    public void Any_endpoint_override_means_the_emulator(string? serviceUrl, string? awsEndpointUrl, string? agentRuntimeUrl)
    {
        var target = KnowledgeBaseTarget.From(serviceUrl, name => name switch
        {
            "AWS_ENDPOINT_URL" => awsEndpointUrl,
            "AWS_ENDPOINT_URL_BEDROCK_AGENT_RUNTIME" => agentRuntimeUrl,
            _ => null,
        });

        Assert.Same(KnowledgeBaseTarget.Emulator, target);
        Assert.Equal("KB on aws-emulator (measured, stand-in)", target.Label);
        Assert.Equal("kb-emulator", target.ExecutionName);
    }

    [Fact]
    public void Only_no_override_at_all_means_bedrock()
    {
        var target = KnowledgeBaseTarget.From("  ", _ => null);

        Assert.Same(KnowledgeBaseTarget.Bedrock, target);
        Assert.Equal("kb-bedrock", target.ExecutionName);
        Assert.DoesNotContain("emulator", target.Label);
    }
}
