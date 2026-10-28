namespace AiLab.Domain.Settings;

public record ChunkingOptions
{
    // About 500 tokens per chunk and 50 of overlap, at about 4 characters per token
    public static ChunkingOptions Default { get; } = new(maxChunkSize: 2000, overlapSize: 200, minTrailChars: 100);

    // Get-only: an init setter would let `with { OverlapChars = ... }` or an object initializer skip the checks below
    public int MaxChars { get; }
    public int OverlapChars { get; }
    public int MinTrailChars { get; }

    public ChunkingOptions(int maxChunkSize, int overlapSize, int minTrailChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(overlapSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minTrailChars);

        // Compared doubled, so an odd MaxChars isn't rounded down by integer division
        if (overlapSize * 2 >= maxChunkSize)
            throw new ArgumentOutOfRangeException(nameof(overlapSize), overlapSize,
                $"The overlap must be less than half of the chunk size ({maxChunkSize}), or the window doesn't move forward.");

        MaxChars = maxChunkSize;
        OverlapChars = overlapSize;
        MinTrailChars = minTrailChars;
    }
}
