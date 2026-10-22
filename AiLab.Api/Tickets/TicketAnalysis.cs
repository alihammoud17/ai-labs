using System.ComponentModel;

namespace AiLab.Api.Tickets;

// The typed answer. The [Description]s go into the JSON schema that is sent in the prompt, so they are the rules.
[Description("An analysis of one support ticket for a multi-tenant SaaS.")]
public record TicketAnalysis(
    [property: Description("One or two sentences restating the problem the customer reports, without guessing at the cause.")]
    string Summary,

    [property: Description("The probable cause must name a specific change from the change log (its version and what it changed) if one is relevant; otherwise say the cause is unknown. Never invent a change.")]
    string ProbableCause,

    [property: Description("Three to five short test cases that would reproduce the problem or confirm a fix, each one sentence.")]
    List<string> TestCases);
