using System.Globalization;
using AiLab.Domain.Constants;

namespace AiLab.Evals.Hosting;

// The eval run's switches, all environment variables so plain `dotnet test` needs none of them:
//   AILAB_EVAL=1                 run the eval tests (they need Ollama, Postgres with the corpus ingested, the Anthropic key)
//   AILAB_EVAL_ANSWERS=haiku     who answers /ask: haiku (default; cached with the judges), llama (Ollama, free, not cached)
//                                or bedrock (the model in AILAB_EVAL_MODEL, through the AWS SDK; Bedrock:* from the host config)
//   AILAB_EVAL_MODEL=...         bedrock only, and required there: us.amazon.nova-lite-v1:0 or us.anthropic.claude-haiku-5-5
//                                (D1's IDs). Each model gets its own execution name, so results and cache entries never mix.
//   AILAB_EVAL_RUNS=3            iterations per question (each its own cache entry, so 3 real answers the first time)
//   AILAB_EVAL_EXECUTION=...     the result group in the report; the same name again re-scores from the cache
//   AILAB_EVAL_CACHE_KEY=...     any new value forces fresh model calls without changing anything else
//   AILAB_EVAL_JUDGE=haiku       the judge: haiku (default, the one whose scores count) or llama (free, only to check the
//                                wiring; its scores mean little, the evaluators are tuned for GPT-4o-class models)
//   AILAB_EVAL_CHUNKER=fixed     the chunks /ask, the judges' context and the retrieval runner use: heading or fixed
//                                (unset: the host's Ask:Chunker). Both must be ingested (POST /dev/ingest?chunker=both).
public sealed record EvalSettings(
    bool Enabled, string Answers, int Runs, string ExecutionName, string? CacheKey, string Judge = "haiku", string? Chunker = null,
    string? Model = null)
{
    public const string SkipReason =
        "Set AILAB_EVAL=1 (with Ollama, Postgres and the corpus ingested, and the Anthropic key in user-secrets) to run the evals.";

    public const string StorageRoot = "eval-runs/meai";
    public const string HaikuModel = "claude-haiku-5-5";

    // D1's Bedrock IDs (docker/aws-setup/README.md) and the short name each gets in its execution name: an ID's ':' would
    // end up in a folder name. Compared exactly, as ModelPrices keys them.
    public static readonly IReadOnlyDictionary<string, string> BedrockModels = new Dictionary<string, string>
    {
        ["us.amazon.nova-lite-v1:0"] = "nova-lite",
        ["us.anthropic.claude-haiku-5-5"] = "haiku-5-5",
    };

    // "lines" is the similarity sentences from /dev/seed, not the corpus: nothing to evaluate there
    public static readonly IReadOnlyList<string> Chunkers = [ChunkerNames.Heading, ChunkerNames.Fixed];

    public static EvalSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    // Takes the lookup so tests can pass their own values (environment variables are shared by every test in the process)
    public static EvalSettings From(Func<string, string?> env)
    {
        string answers = (env("AILAB_EVAL_ANSWERS") ?? "haiku").Trim().ToLowerInvariant();
        if (answers is not ("haiku" or "llama" or "bedrock"))
            throw new InvalidOperationException($"AILAB_EVAL_ANSWERS must be haiku, llama or bedrock, not '{answers}'.");

        string? model = env("AILAB_EVAL_MODEL")?.Trim();
        if (string.IsNullOrEmpty(model))
            model = null;
        if (answers == "bedrock" && model is null)
            throw new InvalidOperationException(
                $"AILAB_EVAL_ANSWERS=bedrock needs AILAB_EVAL_MODEL: {string.Join(" or ", BedrockModels.Keys)}.");
        if (answers != "bedrock" && model is not null)
            throw new InvalidOperationException($"AILAB_EVAL_MODEL is for AILAB_EVAL_ANSWERS=bedrock only (answers are {answers}).");
        if (model is not null && !BedrockModels.ContainsKey(model))
            throw new InvalidOperationException(
                $"AILAB_EVAL_MODEL must be {string.Join(" or ", BedrockModels.Keys)}, not '{model}'.");

        string judge = (env("AILAB_EVAL_JUDGE") ?? "haiku").Trim().ToLowerInvariant();
        if (judge is not ("haiku" or "llama"))
            throw new InvalidOperationException($"AILAB_EVAL_JUDGE must be haiku or llama, not '{judge}'.");

        // Haiku answers go through the run's chat client, which is the judge's: with a llama judge they'd be llama answers
        if (judge == "llama" && answers == "haiku")
            throw new InvalidOperationException("AILAB_EVAL_JUDGE=llama needs AILAB_EVAL_ANSWERS=llama.");

        string? chunker = env("AILAB_EVAL_CHUNKER")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(chunker))
            chunker = null;
        else if (!Chunkers.Contains(chunker))
            throw new InvalidOperationException($"AILAB_EVAL_CHUNKER must be {string.Join(" or ", Chunkers)}, not '{chunker}'.");

        string? runs = env("AILAB_EVAL_RUNS");

        return new EvalSettings(
            Enabled: !string.IsNullOrEmpty(env("AILAB_EVAL")),
            Answers: answers,
            Runs: runs is null ? 3 : int.Parse(runs, CultureInfo.InvariantCulture),
            ExecutionName: env("AILAB_EVAL_EXECUTION") ?? DefaultExecutionName(answers, judge, chunker, model),
            CacheKey: env("AILAB_EVAL_CACHE_KEY"),
            Judge: judge,
            Chunker: chunker,
            Model: model);
    }

    // A Bedrock run gets one group per model (bedrock-nova-lite, bedrock-haiku-5-5), checked first so it never lands in a
    // baseline's or the matrix's results. A llama-judged run is a wiring check, so it never lands there either.
    // With a chunker chosen, each matrix cell gets its own group: matrix-<chunker>-<answers>.
    private static string DefaultExecutionName(string answers, string judge, string? chunker, string? model) => (judge, chunker) switch
    {
        _ when answers == "bedrock" => $"bedrock-{BedrockModels[model!]}" + (chunker is null ? "" : $"-{chunker}"),
        ("llama", null) => $"dev-{answers}-judge-llama",
        ("llama", _) => $"dev-{answers}-judge-llama-{chunker}",
        (_, null) => $"baseline-2026-11-03-{answers}",
        _ => $"matrix-{chunker}-{answers}",
    };
}
