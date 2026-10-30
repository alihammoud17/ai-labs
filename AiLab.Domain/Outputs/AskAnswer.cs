using System.ComponentModel;

namespace AiLab.Domain.Outputs;

// The typed answer. The [Description]s go into the JSON schema that is sent in the prompt, so they are rules too
// (prompts/ask-v1.md has the rest).
[Description("An answer to the question, using only the numbered sources.")]
public sealed record AskAnswer(
    [property: Description("The answer, citing sources inline as [n]. If the sources don't answer the question, one sentence saying the documentation doesn't cover it.")]
    string Answer,

    // The explicit claim CitationCheck compares with the numbers that were sent; [n] in the text alone isn't checked
    [property: Description("The numbers n of every source the answer uses, e.g. [1, 3]. Empty if notInDocs is true.")]
    List<int> CitedSources,

    [property: Description("True only when the sources don't answer the question; then citedSources is []. False when they answer part of it.")]
    bool NotInDocs);
