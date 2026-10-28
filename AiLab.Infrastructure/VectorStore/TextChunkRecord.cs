using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

// Adding a property changes the table, and the connector never alters an existing one: drop it and ingest again
public sealed class TextChunkRecord
{
    // nomic-embed-text's vector size. Another embedding model needs this changed and the collection recreated.
    public const int Dimensions = 768;

    [VectorStoreKey] public string Key { get; set; } = "";
    [VectorStoreData] public string Text { get; set; } = "";
    [VectorStoreData(IsIndexed = true)] public string Source { get; set; } = "";

    // Searches filter on it: one collection holds every chunking strategy
    [VectorStoreData(IsIndexed = true)] public string Chunker { get; set; } = "";
    [VectorStoreData] public int Position { get; set; }
    [VectorStoreData] public string? Heading { get; set; }
    [VectorStoreData] public int StartLine { get; set; }
    [VectorStoreData] public int EndLine { get; set; }
    [VectorStoreData] public string ContentHash { get; set; } = "";

    [VectorStoreVector(Dimensions,DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
