using AiLab.Domain.Enums;
using AiLab.Domain.Responses;
using AiLab.Evals.Hosting;

namespace AiLab.Evals;

// AILAB_EVAL only, and free: checks the eval host itself (real Postgres, Ollama embeddings, the ScenarioChatClient swap)
// with llama3.2 answering. No judge call, no Anthropic cost.
public class EvalHostSmokeTests(EvalFixture fixture) : IClassFixture<EvalFixture>
{
    [Fact]
    public async Task Search_returns_the_chunks_ask_would_send()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);

        SearchResponse search = await fixture.SearchAsync("What is the current internal billing contract version?", TestContext.Current.CancellationToken);

        Assert.Equal("postgres", search.Provider);
        Assert.Equal(fixture.Ask.Top, search.Hits.Count);
        Assert.All(search.Hits, h => Assert.Equal(fixture.Ask.Chunker, h.Chunker));
        Assert.Contains(search.Hits, h => h.Source == "docs/contracts.md" && h.StartLine <= 45 && h.EndLine >= 45);
    }

    [Fact]
    public async Task Ask_answers_through_the_scenario_client()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        fixture.Chat.Target = fixture.Ollama;
        try
        {
            AskResponse ask = await fixture.AskAsync("What is the current internal billing contract version?", TestContext.Current.CancellationToken);

            Assert.Equal(GateResult.Passed, ask.Retrieval.Gate);
            Assert.Equal("llama3.2", ask.Usage.Model);
            Assert.False(string.IsNullOrWhiteSpace(ask.Answer));
        }
        finally
        {
            fixture.Chat.Target = null;
        }
    }

    [Fact]
    public async Task Ask_without_a_target_is_a_setup_error()
    {
        Assert.SkipUnless(fixture.Settings.Enabled, EvalSettings.SkipReason);
        fixture.Chat.Target = null;

        // E5 is gated before any model call, so use a question that reaches the model
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.AskAsync("Which environment variable sets the JWT secret?", TestContext.Current.CancellationToken));
    }
}
