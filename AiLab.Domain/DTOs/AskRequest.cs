using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AiLab.Domain.DTOs;

// [Description] and [Required] only document the API (OpenAPI); the limits come from AskSettings and are enforced by AskService.
[Description("A question about the ingested docs.")]
public sealed record AskRequest(
    [property: Required, Description("The question, 1–500 characters.")]
    string? Question,

    [property: Description("How many chunks to retrieve, 1–10. Optional; default 5.")]
    int? Top = null); // the default makes OpenAPI list it as optional
