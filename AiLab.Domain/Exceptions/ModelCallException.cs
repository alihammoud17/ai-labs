namespace AiLab.Domain.Exceptions;

// The model call failed or its reply was unusable. The message is short and safe to return to the client (as a 502);
// the code is stable, so a client can branch on it without parsing the message.
public class ModelCallException(string message, string code, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
