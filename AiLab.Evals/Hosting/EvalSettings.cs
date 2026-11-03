using System.Globalization;

namespace AiLab.Evals.Hosting;

// The eval run's switches, all environment variables so plain `dotnet test` needs none of them:
//   AILAB_EVAL=1                 run the eval tests (they need Ollama, Postgres with the corpus ingested, the Anthropic key)
//   AILAB_EVAL_ANSWERS=haiku     who answers /ask: haiku (default; cached with the judges) or llama (Ollama, free, not cached)
//   AILAB_EVAL_RUNS=3            iterations per question (each its own cache entry, so 3 real answers the first time)
//   AILAB_EVAL_EXECUTION=...     the result group in the report; the same name again re-scores from the cache
//   AILAB_EVAL_CACHE_KEY=...     any new value forces fresh model calls without changing anything else
public sealed record EvalSettings(bool Enabled, string Answers, int Runs, string ExecutionName, string? CacheKey)
{
    public const string SkipReason =
        "Set AILAB_EVAL=1 (with Ollama, Postgres and the corpus ingested, and the Anthropic key in user-secrets) to run the evals.";

    public const string StorageRoot = "eval-runs/meai";
    public const string JudgeModel = "claude-haiku-5-5";

    public static EvalSettings FromEnvironment()
    {
        string answers = (Environment.GetEnvironmentVariable("AILAB_EVAL_ANSWERS") ?? "haiku").Trim().ToLowerInvariant();
        if (answers is not ("haiku" or "llama"))
            throw new InvalidOperationException($"AILAB_EVAL_ANSWERS must be haiku or llama, not '{answers}'.");

        string? runs = Environment.GetEnvironmentVariable("AILAB_EVAL_RUNS");

        return new EvalSettings(
            Enabled: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AILAB_EVAL")),
            Answers: answers,
            Runs: runs is null ? 3 : int.Parse(runs, CultureInfo.InvariantCulture),
            ExecutionName: Environment.GetEnvironmentVariable("AILAB_EVAL_EXECUTION") ?? $"baseline-2026-11-03-{answers}",
            CacheKey: Environment.GetEnvironmentVariable("AILAB_EVAL_CACHE_KEY"));
    }
}
