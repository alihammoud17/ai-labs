using System.ComponentModel;

namespace AiLab.Domain.Responses;

[Description("A short error, safe to show to the caller. Never a stack trace or model text.")]
public record ErrorResponse(
    [property: Description("What went wrong, in one sentence.")]
    string Error,

    [property: Description("Stable code to branch on: provider_unavailable, tool_failed, reply_cut_off, reply_unreadable, not_a_ticket, model_declined, timeout, invalid_request or internal_error.")]
    string Code);
