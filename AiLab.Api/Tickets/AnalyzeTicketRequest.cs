namespace AiLab.Api.Tickets;

// Component is optional: without it the model has to pick one from the ticket text
public record AnalyzeTicketRequest(string? Title, string? Description, string? Component)
{
    // Null means valid. The shape is what Results.ValidationProblem expects.
    public Dictionary<string, string[]>? Validate()
    {
        Dictionary<string, string[]> errors = [];
        if (string.IsNullOrWhiteSpace(Title))
            errors[nameof(Title)] = ["Title is required."];
        if (string.IsNullOrWhiteSpace(Description))
            errors[nameof(Description)] = ["Description is required."];
        return errors.Count == 0 ? null : errors;
    }
}
