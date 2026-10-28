namespace AiLab.Domain.Settings;

public record ChunkingOptions
{
    // About 500 tokens per chunk and 50 of overlap, at about 4 characters per token
    public static ChunkingOptions Default { get; } = new(maxChars: 2000, overlapChars: 200, minTailChars: 100);

    // Get-only: an init setter would let `with { OverlapChars = ... }` or an object initializer skip the checks below
    public int MaxChars { get; }
    public int OverlapChars { get; }
    public int MinTailChars { get; }

    public ChunkingOptions(int maxChars, int overlapChars, int minTailChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(overlapChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minTailChars);

        // Compared doubled, so an odd MaxChars isn't rounded down by integer division
        if (overlapChars * 2 >= maxChars)
            throw new ArgumentOutOfRangeException(nameof(overlapChars), overlapChars,
                $"The overlap must be less than half of the chunk size ({maxChars}), or the window doesn't move forward.");

        MaxChars = maxChars;
        OverlapChars = overlapChars;
        MinTailChars = minTailChars;
    }
}
