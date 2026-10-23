namespace AiLab.Domain.Outputs;

public record FieldResult(string Name, string? Actual, string? Expected, bool Match);

// One extraction compared with its golden answer
public record JobScore(List<FieldResult> Fields, int SkillsMatched, int SkillsTotal, List<string> MissedSkills, List<string> ExtraSkills)
{
    public int FieldsCorrect => Fields.Count(f => f.Match);

    public IEnumerable<string> WrongFields => Fields.Where(f => !f.Match).Select(f => f.Name);
}
