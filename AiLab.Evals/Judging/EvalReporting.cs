using AiLab.Evals.AskSet;
using AiLab.Evals.Hosting;
using AiLab.Infrastructure.Files;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

namespace AiLab.Evals.Judging;

// Where results and cached model calls go, and how scenarios are named.
public static class EvalReporting
{
    // The three LLM-judged evaluators of the plan. Add your own (Recall@5) next to them when you create the configuration.
    public static IEnumerable<IEvaluator> QualityEvaluators() =>
        [new GroundednessEvaluator(), new RelevanceEvaluator(), new RetrievalEvaluator()];

    // The run's chat client, decided here only. With a judge: the judge (the judges' calls, and Haiku's answers, go
    // through it). Judge none + bedrock: the Bedrock client, so its answers are cached like Haiku's. Judge none + llama:
    // no client at all (llama answers through the fixture's own Ollama client). The judge is a factory, called only when
    // it's the answer, so a run with none never creates it (and never reads Anthropic:ApiKey).
    public static IChatClient? ConfigurationClient(EvalSettings settings, Func<IChatClient> judge, IChatClient? bedrock) =>
        settings.Judge != "none" ? judge()
        : settings.Answers == "bedrock" ? bedrock ?? throw new InvalidOperationException("AILAB_EVAL_ANSWERS=bedrock, but no Bedrock client was created.")
        : null;

    // Results and the response cache under eval-runs/meai/ (git-ignored). With a client, caching is on: every call made
    // through scenarioRun.ChatConfiguration.ChatClient (the judges, and /ask when Haiku or Bedrock answers) is stored, keyed
    // by the request, the options, the model, the scenario and the iteration (plus AILAB_EVAL_CACHE_KEY when set), for
    // 14 days. Without one (judge none + llama) there's no ChatConfiguration and nothing is cached; that's allowed only
    // when no evaluator is AI-based.
    public static ReportingConfiguration Create(EvalSettings settings, IChatClient? client, IEnumerable<IEvaluator> evaluators) =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: RepoPaths.Resolve(EvalSettings.StorageRoot),
            evaluators: evaluators,
            chatConfiguration: client is null ? null : new ChatConfiguration(client),
            enableResponseCaching: client is not null,
            cachingKeys: settings.CacheKey is null ? null : [settings.CacheKey],
            executionName: settings.ExecutionName,
            tags: [$"answers:{settings.Answers}", $"judge:{settings.Judge}", .. settings.Model is null ? Array.Empty<string>() : [$"model:{settings.Model}"]]);

    // <test class>.<category>.<id>, e.g. AiLab.Evals.AskSetEvalTests.Two sources needed.B6. The report splits names on
    // '.', so it groups and averages by class, then category. Changing a name changes the cache key (a miss).
    public static string ScenarioName(AskSetRow row) =>
        $"{TestContext.Current.TestClass?.TestClassName ?? "AiLab.Evals"}.{row.Category}.{row.Id}";

    // Tags shown in the report, to filter by what the row expects
    public static IEnumerable<string> Tags(AskSetRow row) =>
        [$"expected:{row.Expected}", $"check:{row.Check}", $"category:{row.Category}"];
}
