using AiLab.Evals.AskSet;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Offline: the JSON test set agrees with ask-set-v1.md and with the corpus it points into.
public class AskSetTests
{
    private static readonly AskSetFile Set = AskSetData.Load();

    [Fact]
    public void Has_30_rows_with_unique_ids()
    {
        Assert.Equal(30, Set.Rows.Count);
        Assert.Equal(30, Set.Rows.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Category_counts_match_the_mix_table()
    {
        Dictionary<string, int> expected = new()
        {
            ["Single fact"] = 5, ["Exact name or number"] = 4, ["List"] = 3, ["Two sources needed"] = 5,
            ["False premise"] = 3, ["Typo or paraphrase"] = 2, ["Partial"] = 2, ["Not in the docs"] = 4,
            ["Out of domain"] = 1, ["Non-English"] = 1,
        };

        Dictionary<string, int> actual = Set.Rows.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    [Fact]
    public void Has_25_answerable_rows_and_5_that_should_not_be_answered()
    {
        Assert.Equal(25, Set.Rows.Count(r => r.Answerable));
        Assert.Equal(["D2", "D9", "INV", "N9", "E5"], Set.Rows.Where(r => !r.Answerable).Select(r => r.Id));
        Assert.Equal("E5", Assert.Single(Set.Rows, r => r.Expected == "gate").Id);
    }

    [Fact]
    public void Fields_use_only_the_documented_values()
    {
        Assert.All(Set.Rows, r =>
        {
            Assert.Contains(r.Expected, new[] { "answer", "partial", "abstain", "gate" });
            Assert.Contains(r.Check, new[] { "exact", "all items", "judge", "abstain" });
            Assert.Contains(r.GoldMode, new[] { "any", "all" });
            Assert.False(string.IsNullOrWhiteSpace(r.Question));
            Assert.False(string.IsNullOrWhiteSpace(r.GoldAnswer));
        });
    }

    [Fact]
    public void Answerable_rows_have_gold_sources_and_the_others_have_none()
    {
        Assert.All(Set.Rows, r =>
        {
            if (r.Answerable)
                Assert.NotEmpty(r.Gold);
            else
                Assert.Empty(r.Gold);
        });
    }

    [Fact]
    public void Exact_and_list_checks_have_items()
    {
        Assert.All(Set.Rows.Where(r => r.Check is "exact" or "all items"), r => Assert.NotEmpty(r.Items));
        Assert.Equal(["A3", "F1", "F4", "F5", "G2"], Set.Rows.Where(r => r.Check == "exact").Select(r => r.Id));
    }

    [Fact]
    public void Both_needed_rows_are_B6_and_G1()
    {
        Assert.Equal(["B6", "G1"], Set.Rows.Where(r => r.GoldMode == "all").Select(r => r.Id));
    }

    [Fact]
    public void Every_gold_line_exists_in_its_corpus_file()
    {
        Assert.All(Set.Rows.SelectMany(r => r.Gold.Concat(r.Supporting ?? []).Select(g => (r.Id, Gold: g))), x =>
        {
            string path = RepoPaths.Resolve(Path.Combine(AskSetData.CorpusFolder, x.Gold.File));
            Assert.True(File.Exists(path), $"{x.Id}: {x.Gold.File} not found under {AskSetData.CorpusFolder}");

            string[] lines = File.ReadAllLines(path);
            Assert.All(x.Gold.Lines, n =>
                Assert.True(n >= 1 && n <= lines.Length, $"{x.Id}: line {n} is outside {x.Gold.File} ({lines.Length} lines)"));

            // A range (A15's list, N7's commands) may hold blank lines; its ends and any single line may not
            Assert.False(string.IsNullOrWhiteSpace(lines[x.Gold.Lines[0] - 1]), $"{x.Id}: {x.Gold.File}:{x.Gold.Lines[0]} is blank");
            Assert.False(string.IsNullOrWhiteSpace(lines[x.Gold.Lines[^1] - 1]), $"{x.Id}: {x.Gold.File}:{x.Gold.Lines[^1]} is blank");
        });
    }

    // Spot checks that the line numbers point at the answer, not just at some line
    [Theory]
    [InlineData("A1", "docs/architecture.md", "returns `202 Accepted`")]
    [InlineData("A3", "docs/contracts.md", "2026-03-18")]
    [InlineData("F1", "docs/contracts.md", "sha256=b82b6991c877ed60e6a337a1928478d323770ca01846828fa0a77c424b19d8ae")]
    [InlineData("F5", "README.md", "Jwt__Secret")]
    [InlineData("G1", "docs/contracts.md", "2026-04-13")]
    [InlineData("N3", "docs/decisions.md", "A new major API version is reserved for breaking contract changes")]
    [InlineData("N5", "docs/decisions.md", "This is not a distributed queue")]
    [InlineData("N6", "docs/decisions.md", "Preflights carry no credentials")]
    public void The_gold_lines_hold_the_answer(string id, string file, string text)
    {
        string[] lines = File.ReadAllLines(RepoPaths.Resolve(Path.Combine(AskSetData.CorpusFolder, file)));
        IEnumerable<int> goldLines = AskSetData.Row(id).Gold.Where(g => g.File == file).SelectMany(g => g.Lines);

        Assert.Contains(goldLines, n => lines[n - 1].Contains(text, StringComparison.Ordinal));
    }
}
