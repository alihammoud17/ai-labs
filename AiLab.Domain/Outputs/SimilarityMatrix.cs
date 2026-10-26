namespace AiLab.Domain.Outputs;

// A similarity matrix for a set of sentences, with the number of tokens in the input and the time taken to compute it.
public sealed record SimilarityMatrix(
    IReadOnlyList<string> Sentences,
    float[,] Scores,
    int Dimensions,
    long? InputTokens,
    TimeSpan Elapsed);