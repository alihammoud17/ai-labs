using AiLab.Api.Evaluation;
using AiLab.Domain.Enums;

namespace AiLab.Api.Tests;

public class TicketScorerTests
{
    private static readonly string[] AllVersions = ["4.18.0", "4.16.2", "4.19.0", "4.17.0", "4.17.1", "4.16.0", "4.17.2", "4.15.3"];

    [Theory]
    // 1. causeVersion is the expected version, whatever the text says (Llama's text often drops it)
    [InlineData("Proration now rounds up to the nearest cent.", "4.18.0", "4.18.0", Verdict.Right)]
    [InlineData("Per-tenant numbering.", "v4.19.0", "4.19.0", Verdict.Right)]
    // 2. causeVersion is any other version, even one the text rules out, or one not in the change log
    [InlineData("Optional TOTP 2FA for tenant admins", "4.16.0", null, Verdict.Invented)]
    [InlineData("Unknown, maybe the sessions change.", "4.17.1", null, Verdict.Invented)]
    [InlineData("v4.18.0 rounds up.", "9.9.9", "4.18.0", Verdict.Invented)]
    // 3. no causeVersion and the cause says unknown, even if it names versions to rule them out
    [InlineData("Unknown. The auth log has v4.17.1 (8h sessions) and v4.16.0 (2FA); neither explains it.", null, null, Verdict.Unknown)]
    [InlineData("Unknown, but likely v4.18.0.", null, "4.18.0", Verdict.Unknown)]
    // "unknown", "n/a" or "" in causeVersion is no claim
    [InlineData("UNKNOWN cause.", "unknown", "4.18.0", Verdict.Unknown)]
    [InlineData("Unknown.", "", null, Verdict.Unknown)]
    // 4. fallback on the text when there is no claim: expected version, other version, no version
    [InlineData("v4.18.0: proration now rounds up.", null, "4.18.0", Verdict.Right)]
    [InlineData("The cause is v4.18.0.", null, "4.18.0", Verdict.Right)]
    [InlineData("v4.17.1 shortened admin sessions.", null, null, Verdict.Invented)]
    [InlineData("v4.16.2 logs Stripe retries.", null, "4.18.0", Verdict.Invented)]
    [InlineData("Proration now rounds up to the nearest cent.", null, "4.18.0", Verdict.Unknown)]
    // version boundaries in the text: not 14.18.0 or 4.18.01
    [InlineData("v14.18.0 changed something.", null, "4.18.0", Verdict.Unknown)]
    [InlineData("v4.18.01 changed something.", null, "4.18.0", Verdict.Unknown)]
    public void Score_reads_the_claim_first_and_the_text_as_fallback(string cause, string? causeVersion, string? expected, Verdict verdict) =>
        Assert.Equal(verdict, TicketScorer.Score(cause, causeVersion, expected, AllVersions));

    [Theory]
    [InlineData(Verdict.Right, "4.18.0", true)]
    [InlineData(Verdict.Unknown, "4.18.0", false)]
    [InlineData(Verdict.Unknown, null, true)]
    [InlineData(Verdict.Invented, null, false)]
    [InlineData(Verdict.Failed, null, false)]
    public void IsCorrect_is_right_when_expected_and_unknown_when_not(Verdict verdict, string? expected, bool correct) =>
        Assert.Equal(correct, TicketScorer.IsCorrect(verdict, expected));
}
