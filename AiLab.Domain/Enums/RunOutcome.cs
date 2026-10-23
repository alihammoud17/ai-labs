namespace AiLab.Domain.Enums;

// How a model answer ended. Only Analyzed has an analysis and a verification.
public enum RunOutcome { Analyzed, CutOff, Declined, NotATicket, Unreadable }
