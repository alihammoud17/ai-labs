using AiLab.Domain.DTOs;
using AiLab.Domain.Outputs;
using AiLab.Domain.Responses;

namespace AiLab.Domain.Interfaces;

// One ticket in, one analysis out
public interface ITicketAnalyzer
{
    // For POST /analyze-ticket: an unusable reply throws ModelCallException (502) or TicketRejectedException (422)
    Task<AnalyzeTicketResponse> AnalyzeAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken);

    // For the ticket eval: the model call and its diagnostics, without failing on an unusable reply.
    // Provider and tool failures still throw ModelCallException.
    Task<TicketRun> RunAsync(AnalyzeTicketRequest request, CancellationToken cancellationToken);
}
