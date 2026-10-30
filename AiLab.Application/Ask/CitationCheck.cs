using System.Text.RegularExpressions;
using AiLab.Domain.Enums;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;

namespace AiLab.Application.Ask;

// Checks the model's citations against the sources sent with the question. First matching rule wins.
// The citations in the response are built here from the server's map n → SearchHit, never from the model's text.
public static class CitationCheck
{
    // [2], [1][3] and [1, 3]. One or two digits: Ask:MaxTop is 10, and a year like [2024] in the text isn't a citation.
    private static readonly Regex Inline = new(@"\[(\d{1,2}(?:\s*,\s*\d{1,2})*)\]");

    public static CitationResult Evaluate(AskAnswer answer, IReadOnlyDictionary<int, SearchHit> sources)
    {
        // 1. The model says the docs don't cover it. Any citations it gave anyway are dropped: there is no answer to support.
        if (answer.NotInDocs)
            return new(new AskGrounding(GroundingStatus.Abstained, "The model said the sources don't answer the question."), []);

        // The model's claim: the citedSources list and the [n] in the text, together. A number only in the text is still cited.
        SortedSet<int> cited = [.. answer.CitedSources, .. InlineNumbers(answer.Answer)];
        List<AskCitation> citations = cited.Where(sources.ContainsKey).Select(n => Citation(n, sources[n])).ToList();

        // 2. A number that wasn't sent is made up. The real ones are still listed, so a reader can check them.
        List<int> notSent = cited.Where(n => !sources.ContainsKey(n)).ToList();
        if (notSent.Count > 0)
            return new(new AskGrounding(GroundingStatus.Unverified,
                $"Cites {Numbers(notSent)}, which {(notSent.Count == 1 ? "wasn't" : "weren't")} among the {sources.Count} sources sent."), citations);

        // 3. An answer with nothing to check it against
        if (cited.Count == 0)
            return new(new AskGrounding(GroundingStatus.Unverified, "The answer cites no source."), []);

        // 4. Every cited number was sent. Says where the words came from, not that the answer is right.
        return new(new AskGrounding(GroundingStatus.Grounded,
            $"Cites {Numbers(cited)}, all among the {sources.Count} sources sent."), citations);
    }

    private static IEnumerable<int> InlineNumbers(string text) =>
        Inline.Matches(text).SelectMany(m => m.Groups[1].Value.Split(',')).Select(n => int.Parse(n.Trim()));

    private static AskCitation Citation(int n, SearchHit hit) =>
        new(n, hit.Chunk.Source, hit.Chunk.Heading, hit.Chunk.StartLine, hit.Chunk.EndLine, hit.Score);

    private static string Numbers(IEnumerable<int> numbers) => string.Join(", ", numbers.Select(n => $"[{n}]"));
}

public sealed record CitationResult(AskGrounding Grounding, IReadOnlyList<AskCitation> Citations);
