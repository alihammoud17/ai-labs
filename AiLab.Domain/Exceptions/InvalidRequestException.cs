namespace AiLab.Domain.Exceptions;

// The caller's input is wrong (an empty query, top out of range): a 400. Its own type so a host doesn't treat every
// ArgumentException as the caller's fault; one from our own checks deeper down is a bug (a 500). The message is safe to return.
public class InvalidRequestException(string message) : Exception(message);
