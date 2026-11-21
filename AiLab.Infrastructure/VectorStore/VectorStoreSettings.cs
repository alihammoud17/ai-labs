namespace AiLab.Infrastructure.VectorStore;

// The collection and its vector size, read once in AddVectorStore: VectorStore:Collection (default "corpus") and
// Embedding:Dimensions (default 768, nomic-embed-text). The same size builds the collection's schema and is what
// TextVectorStore and both keyword searches check every vector against.
public sealed record VectorStoreSettings(string Collection, int Dimensions)
{
    public const string DefaultCollection = "corpus";

    public static VectorStoreSettings Default { get; } = new(DefaultCollection, TextChunkRecord.DefaultDimensions);

    // The one message for a vector of the wrong size, from the store and both keyword searches
    public string WrongSize(string what, int actual) =>
        $"{what} has {actual} dimensions, but collection '{Collection}' expects {Dimensions} (Embedding:Dimensions). " +
        "Check Embedding:Model and Embedding:Dimensions; a new size also needs a new VectorStore:Collection.";
}
