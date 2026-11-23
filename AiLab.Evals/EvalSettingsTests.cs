using AiLab.Evals.Hosting;
using AiLab.Evals.Judging;
using Microsoft.Extensions.AI;

namespace AiLab.Evals;

// Offline: how the eval switches are read. Each test passes its own variables, so nothing depends on the real environment.
public class EvalSettingsTests
{
    private static EvalSettings Read(params (string Name, string Value)[] vars)
    {
        var values = vars.ToDictionary(v => v.Name, v => v.Value);
        return EvalSettings.From(name => values.GetValueOrDefault(name));
    }

    [Fact]
    public void No_chunker_set_means_the_hosts_Ask_Chunker_and_the_old_execution_names()
    {
        var settings = Read();

        Assert.Null(settings.Chunker);
        Assert.Equal("baseline-2026-11-03-haiku", settings.ExecutionName);
        Assert.Equal("dev-llama-judge-llama", Read(("AILAB_EVAL_ANSWERS", "llama"), ("AILAB_EVAL_JUDGE", "llama")).ExecutionName);
    }

    [Theory]
    [InlineData("heading", "heading")]
    [InlineData("fixed", "fixed")]
    [InlineData(" Fixed ", "fixed")]
    public void A_corpus_chunker_is_accepted(string value, string expected)
    {
        Assert.Equal(expected, Read(("AILAB_EVAL_CHUNKER", value)).Chunker);
    }

    [Fact]
    public void An_empty_chunker_counts_as_unset()
    {
        Assert.Null(Read(("AILAB_EVAL_CHUNKER", "  ")).Chunker);
    }

    [Theory]
    [InlineData("lines")]
    [InlineData("semantic")]
    public void Any_other_chunker_is_refused_naming_the_variable(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(("AILAB_EVAL_CHUNKER", value)));

        Assert.Contains("AILAB_EVAL_CHUNKER", ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Theory]
    [InlineData("heading", "haiku", "matrix-heading-haiku")]
    [InlineData("fixed", "haiku", "matrix-fixed-haiku")]
    [InlineData("heading", "llama", "matrix-heading-llama")]
    [InlineData("fixed", "llama", "matrix-fixed-llama")]
    public void Each_matrix_cell_gets_its_own_execution_name(string chunker, string answers, string expected)
    {
        var settings = Read(("AILAB_EVAL_CHUNKER", chunker), ("AILAB_EVAL_ANSWERS", answers));

        Assert.Equal(expected, settings.ExecutionName);
    }

    [Fact]
    public void A_llama_judged_run_with_a_chunker_stays_out_of_the_matrix_results()
    {
        var settings = Read(("AILAB_EVAL_CHUNKER", "fixed"), ("AILAB_EVAL_ANSWERS", "llama"), ("AILAB_EVAL_JUDGE", "llama"));

        Assert.Equal("dev-llama-judge-llama-fixed", settings.ExecutionName);
    }

    [Fact]
    public void An_explicit_execution_name_wins_over_the_chunker()
    {
        var settings = Read(("AILAB_EVAL_CHUNKER", "fixed"), ("AILAB_EVAL_EXECUTION", "my-run"));

        Assert.Equal("my-run", settings.ExecutionName);
        Assert.Equal("fixed", settings.Chunker);
    }

    private const string NovaLite = "us.amazon.nova-lite-v1:0";
    private const string BedrockHaiku = "us.anthropic.claude-haiku-5-5";

    [Theory]
    [InlineData(NovaLite)]
    [InlineData(BedrockHaiku)]
    public void Bedrock_with_one_of_D1s_models_is_accepted(string model)
    {
        var settings = Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", model), ("AILAB_EVAL_JUDGE", "none"));

        Assert.Equal(("bedrock", model), (settings.Answers, settings.Model));
    }

    [Fact]
    public void Bedrock_without_a_model_is_refused_naming_the_variable()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_JUDGE", "none")));

        Assert.Contains("AILAB_EVAL_MODEL", ex.Message);
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("llama")]
    public void A_model_without_bedrock_is_refused(string answers)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(("AILAB_EVAL_ANSWERS", answers), ("AILAB_EVAL_MODEL", NovaLite)));

        Assert.Contains("bedrock only", ex.Message);
    }

    // Exact IDs: the bare model ID, a global profile or another case is not one of D1's two
    [Theory]
    [InlineData("amazon.nova-lite-v1:0")]
    [InlineData("global.anthropic.claude-haiku-5-5")]
    [InlineData("US.AMAZON.NOVA-LITE-V1:0")]
    public void Any_other_model_is_refused(string model)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", model), ("AILAB_EVAL_JUDGE", "none")));

        Assert.Contains(model, ex.Message);
    }

    [Fact]
    public void Each_bedrock_model_gets_its_own_execution_name_outside_the_baseline_and_matrix()
    {
        string nova = Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", NovaLite), ("AILAB_EVAL_JUDGE", "none")).ExecutionName;
        string haiku = Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", BedrockHaiku), ("AILAB_EVAL_JUDGE", "none")).ExecutionName;
        string withChunker = Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", NovaLite), ("AILAB_EVAL_JUDGE", "none"),
            ("AILAB_EVAL_CHUNKER", "fixed")).ExecutionName;

        Assert.Equal(("bedrock-nova-lite", "bedrock-haiku-5-5", "bedrock-nova-lite-fixed"), (nova, haiku, withChunker));
        Assert.All(new[] { nova, haiku, withChunker }, name =>
        {
            Assert.DoesNotContain(':', name);
            Assert.False(name.StartsWith("baseline-") || name.StartsWith("matrix-"), name);
        });
    }

    // 5.2: no judge
    [Fact]
    public void Judge_none_with_llama_answers_is_a_wiring_check_outside_the_baseline_and_matrix()
    {
        var settings = Read(("AILAB_EVAL_ANSWERS", "llama"), ("AILAB_EVAL_JUDGE", "none"));
        var withChunker = Read(("AILAB_EVAL_ANSWERS", "llama"), ("AILAB_EVAL_JUDGE", "none"), ("AILAB_EVAL_CHUNKER", "heading"));

        Assert.Equal("none", settings.Judge);
        Assert.Equal(("dev-llama-judge-none", "dev-llama-judge-none-heading"), (settings.ExecutionName, withChunker.ExecutionName));
    }

    [Fact]
    public void Judge_none_with_haiku_answers_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Read(("AILAB_EVAL_JUDGE", "none")));

        Assert.Contains("AILAB_EVAL_ANSWERS=haiku", ex.Message);
    }

    [Theory]
    [InlineData("haiku")]
    [InlineData("llama")]
    public void Bedrock_with_a_judge_is_refused(string judge)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", NovaLite), ("AILAB_EVAL_JUDGE", judge)));

        Assert.Contains("AILAB_EVAL_JUDGE=none", ex.Message);
    }

    // A judge factory that fails the test if anyone calls it: proves the judge (and Anthropic:ApiKey) is never touched
    private static IChatClient NeverCalled() => throw new InvalidOperationException("The judge was created.");

    [Fact]
    public void Configuration_client_with_none_and_bedrock_is_the_bedrock_client()
    {
        var settings = Read(("AILAB_EVAL_ANSWERS", "bedrock"), ("AILAB_EVAL_MODEL", NovaLite), ("AILAB_EVAL_JUDGE", "none"));
        using var bedrock = new StubChatClient();

        Assert.Same(bedrock, EvalReporting.ConfigurationClient(settings, NeverCalled, bedrock));
    }

    [Fact]
    public void Configuration_client_with_none_and_llama_is_none()
    {
        var settings = Read(("AILAB_EVAL_ANSWERS", "llama"), ("AILAB_EVAL_JUDGE", "none"));

        Assert.Null(EvalReporting.ConfigurationClient(settings, NeverCalled, bedrock: null));
    }

    [Theory]
    [InlineData("haiku", "haiku")]
    [InlineData("llama", "llama")]
    public void Configuration_client_with_a_judge_is_the_judge(string answers, string judge)
    {
        var settings = Read(("AILAB_EVAL_ANSWERS", answers), ("AILAB_EVAL_JUDGE", judge));
        using var judgeClient = new StubChatClient();

        Assert.Same(judgeClient, EvalReporting.ConfigurationClient(settings, () => judgeClient, bedrock: null));
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
