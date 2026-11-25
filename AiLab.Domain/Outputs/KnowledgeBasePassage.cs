namespace AiLab.Domain.Outputs;

// One passage a managed knowledge base returned (Bedrock's Retrieve), in rank order.
// Source = the document's path relative to the corpus (docs/contracts.md), the same form as TextChunk.Source and the
// golden set's gold.file, never the s3:// URI. Rank is 1-based, in the order the service returned the passages.
// Score is a relevance score: HIGHER is better, the opposite of SearchHit.Score (a cosine distance, lower is closer).
// Never feed one into the other's threshold. Text is the passage as returned: on aws-emulator a whitespace-collapsed
// prefix of the file, not verbatim, so finding its lines needs normalising (the evals' PassageLocator).
public sealed record KnowledgeBasePassage(string Source, string Text, double Score, int Rank);
