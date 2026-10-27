using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

public sealed class TextChunkRecord
{
    // nomic-embed-text's vector size. Another embedding model needs this changed and the collection recreated.
    public const int Dimensions = 768;

    [VectorStoreKey] public string Key { get; set; } = "";
    [VectorStoreData] public string Text { get; set; } = "";
    [VectorStoreData(IsIndexed = true)] public string Source { get; set; } = "";

    [VectorStoreVector(Dimensions,DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}