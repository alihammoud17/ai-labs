using AiLab.Domain.Entities;

namespace AiLab.Domain.Interfaces;

public interface IChangeLogRepository
{
    // Newest first. Matching is case-insensitive; an unknown component returns an empty list.
    IReadOnlyList<ChangeEntry> GetByComponent(string component);

    IReadOnlyList<string> GetComponents();
}
