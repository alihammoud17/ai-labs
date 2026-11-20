namespace AiLab.Application.Services;

public record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion);

public static class ModelPrices
{
    // Per 1M tokens. Keyed by the model ID the client reports (ChatClientMetadata.DefaultModelId), so a Bedrock profile ID
    // and the bare model ID are separate keys.
    private static readonly Dictionary<string, ModelPrice> Prices = new()
    {
        // Anthropic API, copied from CLAUDE.md (checked 2026-10-19)
        ["claude-haiku-5-5"] = new ModelPrice(0.10m, 0.50m),
        ["claude-sonnet-5-5"] = new ModelPrice(2.00m, 10.00m),
        ["llama3.2"] = new ModelPrice(0m, 0m),

        // Amazon Bedrock, us-east-1, on-demand standard tier, from AWS's public price list files
        // (AmazonBedrock / AmazonBedrockFoundationModels, checked 2026-11-20). Long-context, batch, flex and priority tiers
        // cost differently and aren't used here. On aws-emulator the token counts are the emulator's: a wiring check, not a cost.
        ["amazon.nova-lite-v1:0"] = new ModelPrice(0.06m, 0.24m),
        ["us.amazon.nova-lite-v1:0"] = new ModelPrice(0.06m, 0.24m),          // US profile: same price as in-region
        ["us.anthropic.claude-haiku-5-5"] = new ModelPrice(0.11m, 0.55m),     // US profile = "regional" price, 10% over global
        ["global.anthropic.claude-haiku-5-5"] = new ModelPrice(0.10m, 0.50m), // global profile, same as the Anthropic API
    };

    // Returns null when the model isn't in the table, so an unknown price never shows up as $0
    public static decimal? Cost(string model, long inputTokens, long outputTokens)
    {
        if (!Prices.TryGetValue(model, out ModelPrice? price))
            return null;

        return inputTokens / 1_000_000m * price.InputPerMillion
             + outputTokens / 1_000_000m * price.OutputPerMillion;
    }
}
