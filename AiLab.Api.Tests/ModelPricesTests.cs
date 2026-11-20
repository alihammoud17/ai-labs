using AiLab.Application.Services;

namespace AiLab.Api.Tests;

// Bedrock IDs priced from AWS's price list (checked 2026-11-20). One million tokens each way gives input + output price.
public class ModelPricesTests
{
    [Theory]
    [InlineData("us.amazon.nova-lite-v1:0", 0.30)]          // 0.06 + 0.24
    [InlineData("amazon.nova-lite-v1:0", 0.30)]
    [InlineData("us.anthropic.claude-haiku-5-5", 0.66)]     // 0.11 + 0.55: the US profile is the regional price
    [InlineData("global.anthropic.claude-haiku-5-5", 0.60)] // 0.10 + 0.50: the same as the Anthropic API
    public void Bedrock_ids_have_a_price(string model, double expected)
    {
        Assert.Equal((decimal)expected, ModelPrices.Cost(model, 1_000_000, 1_000_000));
    }

    // The emulator's first console turn: in 2, out 38 → $0.00000924 (printed as $0.000009)
    [Fact]
    public void Emulator_turn_costs_what_the_console_printed()
    {
        Assert.Equal(0.00000924m, ModelPrices.Cost("us.amazon.nova-lite-v1:0", 2, 38));
    }

    // Null, not $0: an unknown price must never look free
    [Theory]
    [InlineData("amazon.nova-pro-v1:0")]
    [InlineData("anthropic.claude-haiku-5-5")] // the bare ID isn't callable on demand, so it isn't priced
    [InlineData("eu.amazon.nova-lite-v1:0")]
    public void Unknown_bedrock_ids_have_no_price(string model)
    {
        Assert.Null(ModelPrices.Cost(model, 1_000, 1_000));
    }
}
