using AiLab.Api.ChangeLog;
using AiLab.Api.Tests.Fakes;
using AiLab.Api.Tickets;
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

    // A reply in the TicketAnalysis schema
    private static string Analysis(string probableCause, string? causeVersion = null) => JsonSerializer.Serialize(new
    {
        summary = "Upgrade charged a cent more than the preview",
        probableCause,
        causeVersion,
        testCases = new[] { "Given a Starter tenant, when it upgrades to Pro, then the charge equals the preview." },
    });

    private static JsonElement Verification(JsonElement body) => body.GetProperty("verification");
    private static string? CauseStatus(JsonElement body) => Verification(body).GetProperty("causeStatus").GetString();
    private static string? Reason(JsonElement body) => Verification(body).GetProperty("reason").GetString();

    // 1
    [Fact]
    public async Task Grounded_cause_comes_with_the_evidence_the_tool_returned()
    {
        ScriptedChatClient script = new(
            ToolCall("billing"),
            Reply(Analysis("v4.18.0 rounds proration up, and the preview wasn't updated.", "4.18.0")));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, TicketA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, script.Calls.Count);
        // The real middleware ran the real tool over the fake repository
        Assert.Contains(ToolResultsSent(script), r => r.Contains("v4.18.0: Proration now rounds up"));

        // The guess and the facts are apart: the model's text under aiAnalysis, the server's check under verification
        Assert.Equal(["notice", "aiAnalysis", "verification", "diagnostics"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(AnalyzeTicketResponse.NoticeText, body.GetProperty("notice").GetString());
        Assert.Equal("v4.18.0 rounds proration up, and the preview wasn't updated.", body.GetProperty("aiAnalysis").GetProperty("probableCause").GetString());

        Assert.Equal("grounded", CauseStatus(body));
        Assert.True(Verification(body).GetProperty("toolCalled").GetBoolean());
        Assert.False(Verification(body).GetProperty("toolFailed").GetBoolean());

        ChangeEntry expected = new InMemoryChangeLogRepository().GetByComponent("billing").Single(c => c.Version == "4.18.0");
        JsonElement evidence = Assert.Single(Verification(body).GetProperty("evidence").EnumerateArray());
        Assert.Equal("billing", evidence.GetProperty("component").GetString());
        Assert.Equal(expected.Version, evidence.GetProperty("version").GetString());
        Assert.Equal(expected.ChangedAt, evidence.GetProperty("changedAt").GetDateTimeOffset());
        Assert.Equal(expected.Description, evidence.GetProperty("description").GetString());
    }

    // 2
    [Fact]
    public async Task Version_the_tool_did_not_return_is_unverified()
    {
        // billing returns 4.18.0 and 4.16.2; 4.17.1 is a real auth change, but not in this request's results
        ScriptedChatClient script = new(
            ToolCall("billing"),
            Reply(Analysis("v4.17.1 shortened admin sessions, which broke the upgrade charge.", "4.17.1")));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, TicketA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("unverified", CauseStatus(body));
        Assert.Contains("4.17.1", Reason(body));
        Assert.Empty(Verification(body).GetProperty("evidence").EnumerateArray());
    }

    // 3
    [Fact]
    public async Task Cause_named_without_calling_the_tool_is_unverified()
    {
        ScriptedChatClient script = new(Reply(Analysis("v4.18.0 rounds proration up.", "4.18.0")));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, TicketA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(script.Calls);
        Assert.Equal("unverified", CauseStatus(body));
        Assert.Contains("wasn't called", Reason(body));
        Assert.False(Verification(body).GetProperty("toolCalled").GetBoolean());
    }

    // Llama's habit from experiments.md section 8: the change's text, without its version
    [Fact]
    public async Task Cause_copied_without_a_version_is_unverified_and_says_where_the_text_came_from()
    {
        ScriptedChatClient script = new(
            ToolCall("billing"),
            Reply(Analysis("Proration now rounds up to the nearest cent instead of banker's rounding.")));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, TicketA);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("unverified", CauseStatus(body));
        Assert.Contains("without citing a change version", Reason(body));
        Assert.Contains("its text matches 4.18.0", Reason(body));
    }

    // Haiku's style from section 8: versions mentioned only to rule them out
    [Fact]
    public async Task Unknown_cause_that_rules_out_returned_versions_is_unknown()
    {
        ScriptedChatClient script = new(
            ToolCall("auth"),
            Reply(Analysis("Unknown. The auth log has v4.17.1 (shorter admin sessions) and v4.16.0 (optional 2FA); neither explains missing reset emails.")));

        (HttpStatusCode status, JsonElement body) = await PostAsync(script, new { title = "Reset emails not coming", description = "Nothing arrives.", component = "auth" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("unknown", CauseStatus(body));
        Assert.Empty(Verification(body).GetProperty("evidence").EnumerateArray());
    }
}
