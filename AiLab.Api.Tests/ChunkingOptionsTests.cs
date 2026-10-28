using AiLab.Domain.Settings;

namespace AiLab.Api.Tests;

public class ChunkingOptionsTests
{
    [Theory]
    [InlineData(0, 10, 10, "maxChunkSize")]
    [InlineData(-1, 10, 10, "maxChunkSize")]
    [InlineData(100, 0, 10, "overlapSize")]
    [InlineData(100, 10, 0, "minTrailChars")]
    public void Zero_and_negative_values_are_refused_and_named(int max, int overlap, int minTrail, string parameter)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingOptions(max, overlap, minTrail));

        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(100, 80)]
    [InlineData(100, 100)]
    public void An_overlap_of_half_the_chunk_or_more_is_refused(int max, int overlap)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingOptions(max, overlap, 10));

        Assert.Equal("overlapSize", error.ParamName);
        Assert.Contains("less than half", error.Message);
    }

    [Theory]
    [InlineData(100, 49)]
    [InlineData(101, 50)] // 50 is under half of 101; integer division would have refused it
    public void An_overlap_under_half_is_accepted(int max, int overlap) =>
        Assert.Equal(overlap, new ChunkingOptions(max, overlap, 10).OverlapChars);

    [Fact]
    public void The_default_is_2000_200_100() =>
        Assert.Equal((2000, 200, 100), (ChunkingOptions.Default.MaxChars, ChunkingOptions.Default.OverlapChars, ChunkingOptions.Default.MinTrailChars));
}
