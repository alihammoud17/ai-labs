using System.Text.Json;
using System.Text.Json.Serialization;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals.AskSet;

// docs/evals/ask-set-v1.json: the 30 questions of ask-set-v1.md as data. The .md is the human view; change both together.
public sealed record AskSetFile(string Name, string Description, string Labelled, IReadOnlyList<AskSetRow> Rows);

public sealed record AskSetRow(
    string Id,
    string Category,
    string Question,
    // answer | partial | abstain | gate
    string Expected,
    // exact | all items | judge | abstain
    string Check,
    IReadOnlyList<GoldSource> Gold,
    // any: one gold source answers alone; all: every gold source is needed
    string GoldMode,
    string GoldAnswer,
    IReadOnlyList<string> Items,
    string? PassIf = null,
    IReadOnlyList<GoldSource>? Supporting = null,
    string? Searched = null,
    bool ExpectedToFail = false,
    string? Note = null)
{
    // Rows /ask should answer (fully or in part); the others should abstain or be gated
    [JsonIgnore]
    public bool Answerable => Expected is "answer" or "partial";

    public override string ToString() => Id;
}

// A file under data/corpus/ and the 1-based lines that hold the answer
public sealed record GoldSource(string File, IReadOnlyList<int> Lines, string? Section = null, string? Note = null);

public static class AskSetData
{
    public const string RelativePath = "docs/evals/ask-set-v1.json";
    public const string CorpusFolder = "data/corpus";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<AskSetFile> Cached = new(() =>
        JsonSerializer.Deserialize<AskSetFile>(File.ReadAllText(RepoPaths.Resolve(RelativePath)), Json)
        ?? throw new InvalidOperationException($"{RelativePath} is empty."));

    public static AskSetFile Load() => Cached.Value;

    public static AskSetRow Row(string id) => Load().Rows.Single(r => r.Id == id);

    // One theory row per question, by id (xUnit shows the id in the test name)
    public static TheoryData<string> Ids() => new(Load().Rows.Select(r => r.Id));
}
