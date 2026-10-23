using AiLab.Domain.DTOs;

namespace AiLab.Domain.Outputs;

// The files /extract needs. PromptNotFound or FileNotFound holds the full path of the missing file.
public record JobExtractionCheck(string PromptPath, string? PromptNotFound, string? FileNotFound);

// One extraction read back and, when there is a golden answer next to the input, loaded for scoring.
// Expected is null with ExpectedError set when the golden file doesn't fit JobPosting.
public record JobExtractionResult(
    bool Parsed, JobPosting? Job, bool StrippedFence, bool CutOff,
    string ExpectedPath, bool HasExpectedFile, JobPosting? Expected, string? ExpectedError);
