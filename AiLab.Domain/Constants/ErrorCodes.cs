namespace AiLab.Domain.Constants;

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
