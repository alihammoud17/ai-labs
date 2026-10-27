namespace AiLab.Domain.Settings;

// Embedding:UseTaskPrefixes, read by the Rag host. nomic-embed-text is trained with task prefixes:
// "search_document: " for stored text, "search_query: " for queries. Off by default (Monday's and Tuesday's runs used none).
public record VectorSearchSettings
{
    public bool UseTaskPrefixes { get; init; }
}
