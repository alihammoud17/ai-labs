using AiLab.Evals.Evaluators;
using AiLab.Evals.Retrieval;
using AiLab.Infrastructure.Files;

namespace AiLab.Evals;

// Offline, on the real corpus (data/corpus): Knowledge Base passages → file lines, for both shapes a passage can have.
public class PassageLocatorTests
{
    private readonly PassageLocator _locator = new();

    private static string[] Lines(string source) =>
        File.ReadAllText(RepoPaths.Resolve(Path.Combine(PassageLocator.DefaultCorpus, source))).Split('\n');

    // Lines first..last (1-based, inclusive) joined as in the file
    private static string Slice(string source, int first, int last) =>
        string.Join('\n', Lines(source)[(first - 1)..last]);

    // aws-emulator's shape (checked 2026-11-24): the file's start, every run of whitespace collapsed to one space,
    // 2,389 characters, ending mid-line 24 ("…and the resolved tenant must"). §20's range for architecture.md is 1–24.
    [Fact]
    public void The_emulators_collapsed_prefix_of_architecture_md_is_lines_1_to_24()
    {
        string file = File.ReadAllText(RepoPaths.Resolve("data/corpus/docs/architecture.md"));
        string passage = PassageLocator.Normalise(file, null).Text[..2389];
        Assert.EndsWith("the resolved tenant must", passage);
        Assert.DoesNotContain('\n', passage);

        var location = _locator.Locate("docs/architecture.md", passage);

        Assert.Equal(new RetrievedChunk("docs/architecture.md", 1, 24), location.Chunk);
        Assert.Null(location.Reason);
    }

    // Real Bedrock's possible shape: newlines kept, a chunk from the middle of a file
    [Fact]
    public void A_passage_from_the_middle_of_contracts_md_with_newlines_kept_gives_its_exact_lines()
    {
        var location = _locator.Locate("docs/contracts.md", Slice("docs/contracts.md", 89, 96));

        Assert.Equal(new RetrievedChunk("docs/contracts.md", 89, 96), location.Chunk);
    }

    [Fact]
    public void Windows_line_endings_and_doubled_spaces_give_the_same_lines()
    {
        string messy = Slice("docs/contracts.md", 89, 96).Replace("\n", "\r\n").Replace(" ", "  ");

        var location = _locator.Locate("docs/contracts.md", messy);

        Assert.Equal(new RetrievedChunk("docs/contracts.md", 89, 96), location.Chunk);
    }

    // A cut inside line 96: the passage ends on that line, not on the last full line before it
    [Fact]
    public void A_passage_that_ends_mid_line_ends_on_the_line_holding_its_last_character()
    {
        string line96 = Lines("docs/contracts.md")[95];
        string passage = Slice("docs/contracts.md", 89, 95) + "\n" + line96[..20];

        var location = _locator.Locate("docs/contracts.md", passage);

        Assert.Equal(new RetrievedChunk("docs/contracts.md", 89, 96), location.Chunk);
    }

    // README's emulator passage, as measured in Task 0.2 (2,573 characters, ≈ lines 1–29): Task 1.2's ceiling depends on it
    [Fact]
    public void Readmes_emulator_prefix_is_lines_1_to_29()
    {
        string file = File.ReadAllText(RepoPaths.Resolve("data/corpus/README.md"));

        var location = _locator.Locate("README.md", PassageLocator.Normalise(file, null).Text[..2573]);

        Assert.Equal(new RetrievedChunk("README.md", 1, 29), location.Chunk);
    }

    [Fact]
    public void A_text_that_isnt_in_the_file_is_not_found_and_the_reason_names_the_file()
    {
        var location = _locator.Locate("docs/contracts.md", "Tenants may export their invoices as CSV every Friday.");

        Assert.False(location.Located);
        Assert.Null(location.Chunk);
        Assert.Equal("passage not found in docs/contracts.md", location.Reason);
    }

    [Theory]
    [InlineData("docs/missing.md")]
    [InlineData("../README.md")]
    public void A_source_outside_the_corpus_is_not_found(string source)
    {
        var location = _locator.Locate(source, "# Architecture");

        Assert.False(location.Located);
        Assert.Contains($"{source} is not in the corpus", location.Reason);
    }

    [Fact]
    public void An_empty_passage_is_not_found()
    {
        Assert.Equal("empty passage for README.md", _locator.Locate("README.md", " \n ").Reason);
    }
}
