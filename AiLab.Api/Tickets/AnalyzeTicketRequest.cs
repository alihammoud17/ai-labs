using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AiLab.Api.Tickets;

// Component is optional: without it the model has to pick one from the ticket text.
// [Description] and [Required] only document the API (OpenAPI); the rules are enforced by Validate().
[Description("A support ticket to analyze.")]
public record AnalyzeTicketRequest(
    [property: Required, Description("Short title of the ticket, as the customer wrote it.")]
    string? Title,

    [property: Required, Description("The customer's description of the problem.")]
    string? Description,

    [property: Description("The SaaS component the ticket is about: auth, billing, invoices or tenants. Optional; when missing, the model picks one from the text.")]
    string? Component = null) // the default makes OpenAPI list it as optional
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
