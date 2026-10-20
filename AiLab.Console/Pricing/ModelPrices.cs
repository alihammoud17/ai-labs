namespace AiLab.Console.Pricing;

public record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion);

public static class ModelPrices
{
    // Per 1M tokens, copied from CLAUDE.md (checked 2026-10-19)
    private static readonly Dictionary<string, ModelPrice> Prices = new()
    {
        ["claude-haiku-5-5"] = new ModelPrice(0.10m, 0.50m),
        ["claude-sonnet-5-5"] = new ModelPrice(2.00m, 10.00m),
        ["llama3.2"] = new ModelPrice(0m, 0m),
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
