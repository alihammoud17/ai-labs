using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

public sealed class TextChunkRecord
{
    [VectorStoreKey] public string Key { get; set; } = "";
    [VectorStoreData] public string Text { get; set; } = "";
    [VectorStoreData(IsIndexed = true)] public string Source { get; set; } = "";

    [VectorStoreVector(768, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw)]
    public ReadOnlyMemory<float>? Embedding { get; set; }
}