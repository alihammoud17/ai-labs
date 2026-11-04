using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.VectorData;

namespace AiLab.Infrastructure.VectorStore;

// For VectorStore:Provider=inmemory (the offline tests): a crude stand-in for Postgres full-text search, only so /search and
// /ask work without a database. A chunk scores the number of distinct query words it contains (lowercase, letters and
// digits, a few stopwords dropped; no stemming). Ties go by key. Score = cosine distance, computed here like the store does.
public sealed partial class InMemoryKeywordSearch(VectorStoreCollection<string, TextChunkRecord> collection) : IKeywordSearch
{
    private static readonly HashSet<string> Stopwords =
        ["a", "an", "and", "are", "do", "does", "for", "how", "in", "is", "it", "of", "on", "or", "the", "to", "what", "when", "which", "who", "why"];

    public Task EnsureIndexAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, ReadOnlyMemory<float> queryEmbedding, int top, string? chunker = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        if (queryEmbedding.Length != TextChunkRecord.Dimensions)
            throw new ArgumentException($"The query embedding has {queryEmbedding.Length} dimensions, but the store expects {TextChunkRecord.Dimensions}.", nameof(queryEmbedding));

        HashSet<string> queryWords = [.. Words(query).Where(w => !Stopwords.Contains(w))];
        if (queryWords.Count == 0)
            return [];

        Expression<Func<TextChunkRecord, bool>> filter = chunker is null ? r => true : r => r.Chunker == chunker;
        var scored = new List<(TextChunkRecord Record, int Matches)>();
        try
        {
            await foreach (TextChunkRecord record in collection.GetAsync(filter, int.MaxValue,
                new FilteredRecordRetrievalOptions<TextChunkRecord> { IncludeVectors = true }, ct))
            {
                int matches = queryWords.Count(Words(record.Text).ToHashSet().Contains);
                if (matches > 0)
                    scored.Add((record, matches));
            }
        }
        catch (VectorStoreException)
        {
            if (await collection.CollectionExistsAsync(ct))
                throw;
            throw new VectorCollectionMissingException(collection.Name);
        }

        return scored
            .OrderByDescending(s => s.Matches)
            .ThenBy(s => s.Record.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(s => new SearchHit(
                new TextChunk(s.Record.Chunker, s.Record.Source, s.Record.Position, s.Record.Text, s.Record.Heading, s.Record.StartLine, s.Record.EndLine),
                CosineDistance(queryEmbedding.Span, s.Record.Embedding!.Value.Span)))
            .ToList();
    }

    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    private static double CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return 1 - dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
