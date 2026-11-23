using System.Text.RegularExpressions;
using AiLab.Evals.AskSet;

namespace AiLab.Evals.Evaluators;

public enum GoldVerdict
{
    Correct,
    Wrong,
    NotChecked,
}

public sealed record GoldCheckResult(GoldVerdict Verdict, string Reason);

// "Correct" without a judge, for the 16 rows code can check (5 exact, 6 all items, 4 abstain, 1 gate).
// Week 5's hand rule was facts → support → citation, stopping at the first fail. Code checks facts and citation;
// support (nothing invented) stays a hand or judge step. Plain values in, verdict out: no I/O, no model, no AskResponse,
// so the live evaluator (GoldCheckEvaluator) and the stored-results report use the same function and can't disagree.
public static partial class GoldCheck
{
    // An abstain in words. Haiku says "The documentation doesn't cover…" and still gets status grounded, because it cites
    // the nearby facts the docs do have. "not explicitly stated" is left out on purpose: llama3.2 says it and then invents
    // a number (week 5, HL D2). Written from the week 5 answers, before any Bedrock run.
    public static readonly IReadOnlyList<string> AbstainPhrases =
    [
        "doesn't cover", "does not cover", "don't cover", "do not cover", "not covered",
        "not in the docs", "not in the documentation", "notindocs",
        "doesn't define", "does not define",
        "doesn't state", "does not state",
        "doesn't say", "does not say",
        "doesn't mention", "does not mention",
        "doesn't specify", "does not specify",
    ];

    public static GoldCheckResult Evaluate(
        AskSetRow row, string answer, string status, string gate, IReadOnlyList<RetrievedChunk> citations)
    {
        // Expected first: E5 is a gate row whose Check is "abstain"
        if (row.Expected == "gate")
            return Gate(gate);

        return row.Check switch
        {
            "exact" or "all items" => Answer(row, answer, citations),
            "abstain" => Abstain(answer, status),
            _ => new GoldCheckResult(GoldVerdict.NotChecked, "hand-scored row"),
        };
    }

    // Lowercase, no backticks or *, curly apostrophes made straight, whitespace collapsed. Applied to the answer and to
    // every alternative, so `API-owned records` in the JSON matches "api-owned records" in an answer.
    public static string Normalize(string text) =>
        Whitespace().Replace(text.ToLowerInvariant().Replace("`", "").Replace("*", "").Replace('’', '\''), " ").Trim();

    // Facts, then citation; stop at the first fail so the reason names the real problem
    private static GoldCheckResult Answer(AskSetRow row, string answer, IReadOnlyList<RetrievedChunk> citations)
    {
        var match = row.Match
            ?? throw new InvalidOperationException($"{row.Id} is an '{row.Check}' row without a match list (ask-set-v1.json).");

        string text = Normalize(answer);
        var missing = match.Where(fact => !fact.Any(alternative => text.Contains(Normalize(alternative), StringComparison.Ordinal)))
            .Select(fact => fact[0])
            .ToList();
        if (missing.Count > 0)
            return new GoldCheckResult(GoldVerdict.Wrong, $"missing: {string.Join("; ", missing)}");

        // any: one gold source cited is enough. all: every gold source needs a citation that holds its line.
        var uncited = row.Gold.Where(gold => !citations.Any(c => RecallAtKEvaluator.IsHit(gold, c))).ToList();
        bool cited = row.GoldMode == "all" ? uncited.Count == 0 : uncited.Count < row.Gold.Count;
        if (!cited)
        {
            var notHeld = row.GoldMode == "all" ? uncited : row.Gold;
            string sent = citations.Count == 0 ? "no citation" : $"cited {string.Join(", ", citations)}";
            return new GoldCheckResult(GoldVerdict.Wrong,
                $"facts found, but no citation holds {string.Join(" / ", notHeld.Select(Describe))} ({sent})");
        }

        return new GoldCheckResult(GoldVerdict.Correct, $"found: {string.Join("; ", match.Select(fact => fact[0]))}; cited");
    }

    // Week 5's rule: ignore the /ask status when the words abstain; an abstained status counts too
    private static GoldCheckResult Abstain(string answer, string status)
    {
        if (status == "abstained")
            return new GoldCheckResult(GoldVerdict.Correct, "status abstained");

        string text = Normalize(answer);
        string? phrase = AbstainPhrases.FirstOrDefault(p => text.Contains(p, StringComparison.Ordinal));
        return phrase is not null
            ? new GoldCheckResult(GoldVerdict.Correct, $"says \"{phrase}\" (status {status})")
            : new GoldCheckResult(GoldVerdict.Wrong, $"answered instead of abstaining (status {status}): {Start(answer)}");
    }

    // The off-topic row must be stopped before the model; a model abstain means the gate let it through
    private static GoldCheckResult Gate(string gate) => gate is "too_far" or "no_hits"
        ? new GoldCheckResult(GoldVerdict.Correct, $"gated ({gate})")
        : new GoldCheckResult(GoldVerdict.Wrong, $"not gated (gate {gate})");

    private static string Describe(GoldSource gold) => $"{gold.File}:{string.Join(',', gold.Lines)}";

    private static string Start(string answer)
    {
        string flat = Whitespace().Replace(answer, " ").Trim();
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
