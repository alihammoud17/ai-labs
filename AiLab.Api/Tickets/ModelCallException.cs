namespace AiLab.Api.Tickets;

// The model call failed or its reply was unusable. The message is short and safe to return to the client (as a 502);
// the code is stable, so a client can branch on it without parsing the message.
public class ModelCallException(string message, string code, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

// The model answered, but there is nothing to analyze: not a ticket, or the model declined (a 422)
public class TicketRejectedException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}

// The stable error codes in every error body
public static class ErrorCodes
{
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ToolFailed = "tool_failed";
    public const string ReplyCutOff = "reply_cut_off";
    public const string ReplyUnreadable = "reply_unreadable";
    public const string NotATicket = "not_a_ticket";
    public const string ModelDeclined = "model_declined";
    public const string Timeout = "timeout";
    public const string InvalidRequest = "invalid_request";
    public const string InternalError = "internal_error";
}
