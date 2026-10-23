using AiLab.Api.Evaluation;

namespace AiLab.Api.Tests;

public class TicketScorerTests
{
    private static readonly string[] AllVersions = ["4.18.0", "4.16.2", "4.19.0", "4.17.0", "4.17.1", "4.16.0", "4.17.2", "4.15.3"];

    [Theory]
    // 1. the expected version wins, even next to "unknown"
    [InlineData("v4.18.0: proration now rounds up.", "4.18.0", Verdict.Right)]
    [InlineData("Unknown, but likely v4.18.0 (proration rounding).", "4.18.0", Verdict.Right)]
    // 2. "unknown" before other versions: Haiku rules versions out by name
    [InlineData("Unknown. The auth log has v4.17.1 (8h sessions) and v4.16.0 (2FA); neither explains it.", null, Verdict.Unknown)]
    [InlineData("UNKNOWN cause.", "4.18.0", Verdict.Unknown)]
    // 3. another version from the change log
    [InlineData("v4.17.1 shortened admin sessions.", null, Verdict.Invented)]
    [InlineData("v4.16.2 logs Stripe retries.", "4.18.0", Verdict.Invented)]
    // 4. no version at all
    [InlineData("Proration now rounds up to the nearest cent.", "4.18.0", Verdict.Unknown)]
    // version boundaries: not 14.18.0 or 4.18.01, but a full stop after it is fine
    [InlineData("v14.18.0 changed something.", "4.18.0", Verdict.Unknown)]
    [InlineData("v4.18.01 changed something.", "4.18.0", Verdict.Unknown)]
    [InlineData("The cause is v4.18.0.", "4.18.0", Verdict.Right)]
    public void Score_follows_the_order(string cause, string? expected, Verdict verdict) =>
        Assert.Equal(verdict, TicketScorer.Score(cause, expected, AllVersions));

    [Theory]
    [InlineData(Verdict.Right, "4.18.0", true)]
    [InlineData(Verdict.Unknown, "4.18.0", false)]
    [InlineData(Verdict.Unknown, null, true)]
    [InlineData(Verdict.Invented, null, false)]
    [InlineData(Verdict.Failed, null, false)]
    public void IsCorrect_is_right_when_expected_and_unknown_when_not(Verdict verdict, string? expected, bool correct) =>
        Assert.Equal(correct, TicketScorer.IsCorrect(verdict, expected));
}
