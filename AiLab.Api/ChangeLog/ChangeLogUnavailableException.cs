namespace AiLab.Api.ChangeLog;

// The change log couldn't be read. Thrown by the tool, so a repository outage is told apart from a bug of ours.
public class ChangeLogUnavailableException(Exception inner) : Exception("The change log could not be read.", inner);
