using AiLab.Domain.Outputs;
using Microsoft.Extensions.AI;

namespace AiLab.Application.Services;

// The running cost of this session, across chat turns, extractions and eval runs
public class SessionCostTracker(IChatClient client)
{
    // Provider and model as the client reports them, so a call is priced without reading config
    public string? Model { get; } = client.GetService<ChatClientMetadata>()?.DefaultModelId;

    public decimal Total { get; private set; }

    // Prices one response by the model the client was created with and adds it to the session total.
    // Usage arrives with the response; it's billed even if the reply turns out empty.
    public TurnUsage Add(ChatResponse response)
    {
        long? inputTokens = response.Usage?.InputTokenCount;
        long? outputTokens = response.Usage?.OutputTokenCount;

        // Null when the tokens or the price are unknown, so an unknown price never shows up as $0
        decimal? cost = null;
        if (inputTokens is long input && outputTokens is long output && Model is not null)
        {
            cost = ModelPrices.Cost(Model, input, output);
            if (cost is not null)
                Total += cost.Value;
        }

        return new TurnUsage(inputTokens, outputTokens, response.FinishReason?.ToString(), cost, Total, Model);
    }
}
