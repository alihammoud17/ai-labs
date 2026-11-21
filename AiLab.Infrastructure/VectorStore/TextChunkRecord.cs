using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

// Adding a property changes the table, and the connector never alters an existing one: drop it and ingest again
public sealed class TextChunkRecord
{
    // nomic-embed-text's vector size: the size when Embedding:Dimensions isn't set. The real size comes from Definition(),
    // which AddVectorStore passes to GetCollection: a definition overrides the attribute property by property.
    public const int DefaultDimensions = 768;

    // The schema with another vector size, from Embedding:Dimensions (Titan v2: 1024). Only the vector property is listed:
    // the key, the data properties, cosine distance and HNSW still come from the attributes below.
    // A new size needs a new collection: Postgres creates the table once (CREATE TABLE IF NOT EXISTS) and never changes
    // its vector(N) column, so 1024 vectors in a 768 table fail on insert (shown as "vector store unavailable").
    public static VectorStoreCollectionDefinition Definition(int dimensions) => new()
    {
        Properties = [new VectorStoreVectorProperty(nameof(Embedding), typeof(ReadOnlyMemory<float>?), dimensions)],
    };

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

    [VectorStoreVector(DefaultDimensions, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}
