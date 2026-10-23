using AiLab.Domain.Enums;
using AiLab.Domain.Responses;

namespace AiLab.Domain.Outputs;

// One model answer: how it ended, the last reply as sent, the cleaned analysis and its verification (both null unless Analyzed)
// and the diagnostics
public record TicketRun(RunOutcome Outcome, string RawText, TicketAnalysis? Analysis, CauseVerification? Verification, AnalysisDiagnostics Diagnostics);
