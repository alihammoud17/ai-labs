namespace AiLab.Domain.Exceptions;

// The model answered, but there is nothing to analyze: not a ticket, or the model declined (a 422)
public class TicketRejectedException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
