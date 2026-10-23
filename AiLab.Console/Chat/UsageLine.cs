using AiLab.Domain.Outputs;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// The tokens and cost line after a chat turn or an extraction
public static class UsageLine
{
    public static void Print(TurnUsage usage)
    {
        if (usage.InputTokens is not long inputTokens || usage.OutputTokens is not long outputTokens)
        {
            Console.WriteLine("Tokens: unknown");
            return;
        }

        if (usage.Cost is null)
        {
            Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {usage.FinishReason} | Turn: unknown price for '{usage.Model}'");
            return;
        }

        Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {usage.FinishReason} | Turn: ${usage.Cost:0.000000} | Session: ${usage.SessionTotal:0.000000}");
    }
}
