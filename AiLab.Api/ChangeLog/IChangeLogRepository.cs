namespace AiLab.Api.ChangeLog;

public interface IChangeLogRepository
{
    // Newest first. Matching is case-insensitive; an unknown component returns an empty list.
    IReadOnlyList<ChangeEntry> GetByComponent(string component);

    IReadOnlyList<string> GetComponents();
}
