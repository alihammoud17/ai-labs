using System.Text.RegularExpressions;
using AiLab.Domain.DTOs;
using AiLab.Domain.Exceptions;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Outputs;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace AiLab.Infrastructure.VectorStore;

// The query from step 4.3 (eval/queries/), in code. The connector's own HybridSearchAsync isn't used: it ANDs every word
// (plainto_tsquery), which found 0 chunks for "Which environment variable sets the JWT secret?", and its score is a fusion
// score, not the cosine distance /ask's gate reads.
// SQL here, so it uses its own NpgsqlDataSource (with pgvector's type mapped) on the connector's table: "Text", "Chunker",
// "Embedding" are TextChunkRecord's property names, which the connector uses as quoted column names.
public sealed partial class PostgresKeywordSearch : IKeywordSearch, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _table;
    private readonly string _searchSql;
    private readonly string _indexSql;
    private readonly ILogger<PostgresKeywordSearch> _logger;
    private volatile bool _indexEnsured;

    public PostgresKeywordSearch(NpgsqlDataSource dataSource, string table, ILogger<PostgresKeywordSearch> logger)
    {
        // The table name goes into the SQL text (it can't be a parameter), so only a plain identifier is accepted
        if (!PlainIdentifier().IsMatch(table))
            throw new ArgumentException($"VectorStore:Collection '{table}' must be letters, digits and underscores for keyword search.", nameof(table));

        _dataSource = dataSource;
        _table = table;
        _logger = logger;

        // q: the question's stems joined with OR; only chunks with at least one stem match. ts_rank_cd orders them (higher =
        // better, no fixed scale); "Key" breaks ties so the order is the same on every run. Distance: pgvector's <=> (cosine).
        _searchSql = $"""
            SELECT "Key", "Text", "Source", "Chunker", "Position", "Heading", "StartLine", "EndLine",
                   "Embedding" <=> @vector AS distance
            FROM "{table}",
                 to_tsquery('english', replace(plainto_tsquery('english', @query)::text, '&', '|')) AS q
            WHERE (@chunker IS NULL OR "Chunker" = @chunker)
              AND to_tsvector('english', "Text") @@ q
            ORDER BY ts_rank_cd(to_tsvector('english', "Text"), q) DESC, "Key"
            LIMIT @top
            """;

        // The expression must be the same as in the WHERE above, or Postgres won't use the index
        _indexSql = $"""CREATE INDEX IF NOT EXISTS "{IndexName(table)}" ON "{table}" USING GIN (to_tsvector('english', "Text"))""";
    }

    public static string IndexName(string table) => $"{table}_text_fts";

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        try
        {
            await using NpgsqlCommand command = _dataSource.CreateCommand(_indexSql);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            throw new VectorCollectionMissingException(_table);
        }

        _indexEnsured = true;
        _logger.LogDebug("Full-text index {Index} on {Table} ensured", IndexName(_table), _table);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, ReadOnlyMemory<float> queryEmbedding, int top, string? chunker = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        if (queryEmbedding.Length != TextChunkRecord.Dimensions)
            throw new ArgumentException($"The query embedding has {queryEmbedding.Length} dimensions, but the store expects {TextChunkRecord.Dimensions}.", nameof(queryEmbedding));

        // A table ingested before this step has no index yet: create it once, on first use (IF NOT EXISTS makes a race harmless)
        if (!_indexEnsured)
            await EnsureIndexAsync(ct);

        await using NpgsqlCommand command = _dataSource.CreateCommand(_searchSql);
        command.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = query });
        command.Parameters.Add(new NpgsqlParameter { ParameterName = "vector", Value = new Vector(queryEmbedding) });
        command.Parameters.Add(new NpgsqlParameter("chunker", NpgsqlDbType.Text) { Value = (object?)chunker ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("top", NpgsqlDbType.Integer) { Value = top });

        var hits = new List<SearchHit>(top);
        try
        {
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var chunk = new TextChunk(
                    Chunker: reader.GetString(3),
                    Source: reader.GetString(2),
                    Position: reader.GetInt32(4),
                    Text: reader.GetString(1),
                    Heading: reader.IsDBNull(5) ? null : reader.GetString(5),
                    StartLine: reader.GetInt32(6),
                    EndLine: reader.GetInt32(7));

                // NULL when the row has no embedding, NaN for a zero vector: neither is a usable distance
                if (reader.IsDBNull(8))
                    throw new InvalidOperationException($"Chunk '{reader.GetString(0)}' has no embedding.");
                double distance = reader.GetDouble(8);
                if (double.IsNaN(distance))
                    throw new InvalidOperationException($"The distance for chunk '{chunk.Key}' is NaN; is the query or the stored embedding all zeros?");

                hits.Add(new SearchHit(chunk, distance));
            }
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            throw new VectorCollectionMissingException(_table);
        }

        _logger.LogDebug("Keyword search in {Table} (top {Top}, chunker {Chunker}) returned {Count} hit(s)", _table, top, chunker ?? "any", hits.Count);
        return hits;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    public void Dispose() => _dataSource.Dispose();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier();
}
