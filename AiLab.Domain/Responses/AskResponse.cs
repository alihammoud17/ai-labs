using AiLab.Domain.Enums;
using System.ComponentModel;

namespace AiLab.Domain.Responses;

// POST /ask (AiLab.Rag). The model writes only the answer text; grounding and citations come from the server.
[Description("An answer from the ingested docs, what the server checked about it, and how it was produced.")]
public sealed record AskResponse(
    [property: Description("The model's answer with inline [n] citations. Empty when the distance gate abstained; one sentence saying the docs don't cover it when the model did.")]
    string Answer,

    AskGrounding Grounding,

    [property: Description("The sources the answer cites, from the server's list of what was sent to the model (never from the model's text). Empty when abstained.")]
    IReadOnlyList<AskCitation> Citations,

    AskRetrieval Retrieval,

    AskUsage Usage,

    AskTimings Timings);

[Description("What the server checked about the answer. Written by the server, never by the model.")]
public sealed record AskGrounding(
    [property: Description("grounded: every cited number is a source that was sent. abstained: the docs don't answer it (the distance gate closed, or the model said so). unverified: no citations, or a number that wasn't sent.")]
    GroundingStatus Status,

    [property: Description("One sentence on why the status was given; null when there is nothing to add.")]
    string? Reason = null);

[Description("One cited source: the chunk that was sent as <source n=…>.")]
public sealed record AskCitation(
    [property: Description("The number the answer uses, as in [2].")]
    int N,
    [property: Description("The file the chunk came from, relative to the corpus folder.")]
    string Source,
    [property: Description("The section path above the chunk, joined with \" > \"; null when there is no heading.")]
    string? Heading,
    [property: Description("The first line of the source the chunk covers (1-based).")]
    int StartLine,
    [property: Description("The last line of the source the chunk covers (inclusive).")]
    int EndLine,
    [property: Description("Cosine distance to the question: lower = closer.")]
    double Distance);

[Description("How the chunks were found and whether the distance gate let the question through.")]
public sealed record AskRetrieval(
    [property: Description("The chunker searched (Ask:Chunker), normally heading.")]
    string Chunker,
    [property: Description("How many chunks were asked for.")]
    int Top,
    [property: Description("Cosine distance of the closest chunk; null when nothing was found.")]
    double? BestDistance,
    [property: Description("passed: the model was called. no_hits / too_far: abstained before any model call (too_far: bestDistance above Ask:MaxDistance).")]
    GateResult Gate);

[Description("The model call(s) for this answer. All null when the gate abstained (no model call).")]
public sealed record AskUsage(
    [property: Description("The model that answered, e.g. llama3.2 or claude-haiku-5-5.")]
    string? Model,
    [property: Description("Input tokens, the repair call included.")]
    long? InputTokens,
    [property: Description("Output tokens, the repair call included.")]
    long? OutputTokens);

[Description("Where the time went, in milliseconds.")]
public sealed record AskTimings(
    [property: Description("Embedding the question plus the vector search.")]
    long RetrievalMs,
    [property: Description("The model call(s), the repair included; null when the gate abstained.")]
    long? GenerationMs);
