namespace AiLab.Api.Tickets;

// The model call failed or its reply was unusable. The message is short and safe to return to the client (as a 502).
public class ModelCallException(string message, Exception? inner = null) : Exception(message, inner);
