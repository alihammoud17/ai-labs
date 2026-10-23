using System.ComponentModel;

namespace AiLab.Api.Tickets;

// The typed answer. The [Description]s go into the JSON schema that is sent in the prompt, so they are the rules.
[Description("An analysis of one support ticket for a multi-tenant SaaS.")]
public record TicketAnalysis(
    // Familiar forms (a bug-report title, Given/When/Then) are easier to check and to paste into a tracker
    [property: Description("One line, as a bug report title: what fails, for whom, since when if known (e.g. 'Pro upgrade charged 1 cent more than the preview'). No guess at the cause.")]
    string Summary,

    [property: Description("The probable cause must name a specific change from the change log (its version and what it changed) if one is relevant; otherwise say the cause is unknown. Never invent a change.")]
    string ProbableCause,

    // The explicit claim the server checks against the tool results; the text alone can mention versions only to rule them out
    [property: Description("The version of the one change you name as the cause, exactly as GetRecentChanges returned it, or null if the cause is unknown.")]
    string? CauseVersion,

    [property: Description("Three to five test cases that would reproduce the problem or confirm a fix, each one sentence in the form 'Given …, when …, then …'.")]
    List<string> TestCases);
