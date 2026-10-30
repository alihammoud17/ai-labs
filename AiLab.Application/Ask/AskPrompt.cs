using System.Text;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Application.Ask;

public static class AskPrompt
{
    // instructions: the system prompt (prompts/ask-v1.md), read by the caller so the prompt changes without recompiling.
    // The user message holds only the sources and the question, in the format the system prompt describes.
    public static AskPromptResult Build(string instructions, string question, IReadOnlyList<SearchHit> hits)
    {
        var sources = new Dictionary<int, SearchHit>(hits.Count);
        var sb = new StringBuilder();
        for (int i = 0; i < hits.Count; i++)
        {
            int n = i + 1;
            var hit = hits[i];
            sources[n] = hit;

            var chunk = hit.Chunk;
            sb.AppendLine($"<source n=\"{n}\" file=\"{Attribute(chunk.Source)}\" section=\"{Attribute(chunk.Heading ?? "")}\" lines=\"{chunk.StartLine}-{chunk.EndLine}\">");
            sb.AppendLine(Text(chunk.Text));
            sb.AppendLine("</source>");
        }
        sb.AppendLine();
        sb.Append($"Question: {question}");

        var system = new ChatMessage(ChatRole.System, instructions);
        var user = new ChatMessage(ChatRole.User, sb.ToString());
        return new AskPromptResult([system, user], sources);
    }

    // A quote would end the attribute early
    private static string Attribute(string value) => value.Replace('"', '\'');

    // A closing tag inside a chunk would end its block early, and the rest would read as outside the excerpts (rule 7)
    private static string Text(string value) => value.Trim().Replace("</source", "<\\/source", StringComparison.OrdinalIgnoreCase);
}

public sealed record AskPromptResult(
    IReadOnlyList<ChatMessage> Messages,     // [system, user]
    IReadOnlyDictionary<int, SearchHit> Sources);  // 1 → hits[0], 2 → hits[1], …
