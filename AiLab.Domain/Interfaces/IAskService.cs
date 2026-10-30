using AiLab.Domain.DTOs;
using AiLab.Domain.Responses;

namespace AiLab.Domain.Interfaces;

// One question in, one answer from the ingested docs out (POST /ask)
public interface IAskService
{
    /// <summary>
    /// Retrieves chunks for the question, abstains without a model call when the distance gate closes, otherwise asks the
    /// model and checks its citations. Bad input throws <c>InvalidRequestException</c> (400); an unusable reply throws
    /// <c>ModelCallException</c> (502).
    /// </summary>
    Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct = default);
}
