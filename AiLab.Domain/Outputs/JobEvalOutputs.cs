using AiLab.Domain.DTOs;

namespace AiLab.Domain.Outputs;

// One input with its golden answer
public record JobEvalInput(string Name, string Path, JobPosting Expected);

// An input skipped because its golden file doesn't fit JobPosting
public record JobEvalSkip(string Path, string ExpectedPath, string? Error);

// What /eval will run. PromptNotFound or NoInputsIn (a full path) means there is nothing to run;
// RunFolder is only created when there are inputs.
public record JobEvalPlan(
    string Version, string? PromptNotFound, IReadOnlyList<JobEvalSkip> Skipped, string? NoInputsIn,
    string Instruction, IReadOnlyList<JobEvalInput> Inputs, string Model, string RunFolder);

// One extraction inside /eval. Score is null when the reply didn't parse; it then counts as 0 fields and 0 skills.
public record JobEvalRun(string Input, int Run, JobScore? Score, int SkillsTotal, long? InputTokens, long? OutputTokens, decimal? Cost,
    bool StrippedFence, bool CutOff);

// The runs that finished, and why the eval stopped early (null when it didn't)
public record JobEvalResult(IReadOnlyList<JobEvalRun> Runs, string? StoppedBecause);

// A pass is one run number over all inputs (run 1 of every input, then run 2, ...)
public record JobEvalPass(int Run, int Inputs, int Fields, int Skills, int SkillsTotal);

// The totals over passes, so the spread shows how much the same prompt moves between runs
public record JobEvalTotals(
    int RunCount, int ParseFailures, IReadOnlyList<JobEvalPass> Passes, int InputCount,
    int FieldsTotal, double FieldsAverage, int FieldsLowest, int FieldsHighest,
    int SkillsTotal, double SkillsAverage, int SkillsLowest, int SkillsHighest,
    long InputTokens, long OutputTokens, bool SomeUsageUnknown, decimal? Cost, decimal SessionTotal);
