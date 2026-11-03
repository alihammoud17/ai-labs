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

    // Results and the response cache under eval-runs/meai/ (git-ignored). Caching is on: every call made through
    // scenarioRun.ChatConfiguration.ChatClient (the judges, and /ask when Haiku answers) is stored, keyed by the request,
    // the options, the model, the scenario and the iteration (plus AILAB_EVAL_CACHE_KEY when set), for 14 days.
    public static ReportingConfiguration Create(EvalSettings settings, IChatClient judge, IEnumerable<IEvaluator> evaluators) =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: RepoPaths.Resolve(EvalSettings.StorageRoot),
            evaluators: evaluators,
            chatConfiguration: new ChatConfiguration(judge),
            enableResponseCaching: true,
            cachingKeys: settings.CacheKey is null ? null : [settings.CacheKey],
            executionName: settings.ExecutionName,
            tags: [$"answers:{settings.Answers}", $"judge:{settings.Judge}"]);

    // <test class>.<category>.<id>, e.g. AiLab.Evals.AskSetEvalTests.Two sources needed.B6. The report splits names on
    // '.', so it groups and averages by class, then category. Changing a name changes the cache key (a miss).
    public static string ScenarioName(AskSetRow row) =>
        $"{TestContext.Current.TestClass?.TestClassName ?? "AiLab.Evals"}.{row.Category}.{row.Id}";

    // Tags shown in the report, to filter by what the row expects
    public static IEnumerable<string> Tags(AskSetRow row) =>
        [$"expected:{row.Expected}", $"check:{row.Check}", $"category:{row.Category}"];
}
