using AiLab.Api.Tests.Fakes;
using Microsoft.Extensions.AI;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static AiLab.Api.Tests.Fakes.ScriptedChatClient;

namespace AiLab.Api.Tests;

// POST /analyze-ticket through the real pipeline, with a scripted model at the bottom
public class AnalyzeTicketTests
{
    private static readonly object TicketA = new
    {
        title = "Charged more than the preview showed after upgrading",
        description = "The preview said $12.34 but the card was charged $12.35.",
        component = "billing",
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(ScriptedChatClient script, object ticket)
    {
        using ApiFactory factory = new(script);
        using HttpClient http = factory.CreateClient();
        using HttpResponseMessage response = await http.PostAsJsonAsync("/analyze-ticket", ticket, TestContext.Current.CancellationToken);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (response.StatusCode, body);
    }

    // The tool results the model received, across all its calls
    private static List<string> ToolResultsSent(ScriptedChatClient script) =>
        script.Calls.SelectMany(c => c.Messages)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .Select(r => r.Result?.ToString() ?? "")
            .Distinct()
            .ToList();

    [Fact]
    public async Task Empty_title_is_a_400()
    {
        ScriptedChatClient script = new();

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, new { title = " ", description = "Something broke." });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body.GetProperty("errors").TryGetProperty("Title", out _));
        Assert.Empty(script.Calls);
    }

    [Fact]
    public async Task Happy_path_runs_the_tool_and_returns_the_analysis()
    {
        ScriptedChatClient script = new(
            ToolCall("billing"),
            Reply("""{ "summary": "Upgrade charged a cent more than the preview", "probableCause": "v4.18.0 rounds proration up", "testCases": ["Given a Starter tenant, when it upgrades, then the charge equals the preview."] }"""));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, TicketA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, script.Calls.Count);
        // The real middleware ran the real tool over the fake repository
        Assert.Contains(ToolResultsSent(script), r => r.Contains("v4.18.0: Proration now rounds up"));
        Assert.Equal("v4.18.0 rounds proration up", body.GetProperty("analysis").GetProperty("probableCause").GetString());
    }
}
