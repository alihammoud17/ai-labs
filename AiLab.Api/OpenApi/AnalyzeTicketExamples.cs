using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AiLab.Api.OpenApi;

// Examples shown in Swagger UI for POST /analyze-ticket. The request examples are the four test tickets
// from analyze-ticket.http (keep them in sync); the 200 example is a real Ollama reply to ticket A.
public static class AnalyzeTicketExamples
{
    public static Task Apply(OpenApiOperation operation, CancellationToken _)
    {
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var body) == true)
        {
            body.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["A"] = Example("A: explained by a billing change",
                    """
                    {
                      "title": "Charged more than the preview showed after upgrading",
                      "description": "Hi, we upgraded from Starter to Pro on Tuesday. The preview said $12.34 for the rest of the month but the card was charged $12.35. I know it's only a cent, but our finance team found the same thing on 3 other upgrades this week. This never happened before. Can you check?",
                      "component": "billing"
                    }
                    """),
                ["B"] = Example("B: no change explains it",
                    """
                    {
                      "title": "Reset password emails not coming",
                      "description": "Since yesterday a few of our users tried 'Forgot password'. The page tells them to check their inbox but nothing comes. I checked spam with two of them, nothing there either. Invoice emails still reach them fine, so I don't think it's our mail server.",
                      "component": "auth"
                    }
                    """),
                ["C"] = Example("C: no component given",
                    """
                    {
                      "title": "numbering is messed up",
                      "description": "Hello, since last night our new invoices start from INV-ACME-0001 again. Our last one was INV-004512. Our accounting software flags a gap in the sequence and won't import them. What happened?"
                    }
                    """),
                ["D"] = Example("D: vague, no change explains it",
                    """
                    {
                      "title": "I can't sign-in",
                      "description": "I am trying to sign-in to the application but I can't. It's always failing",
                      "component": "auth"
                    }
                    """),
            };
        }

        SetResponseExample(operation, "200", "Ticket A on llama3.2",
            """
            {
              "analysis": {
                "summary": "Charged more than the preview showed after upgrading",
                "probableCause": "v4.18.0: Proration now rounds up to the nearest cent instead of banker's rounding",
                "testCases": [
                  "Reproduce the issue by upgrading to Pro plan again",
                  "Check the proration calculation with a test plan upgrade",
                  "Verify that the proration change is the cause by comparing with a previous upgrade"
                ]
              },
              "diagnostics": {
                "model": "llama3.2",
                "toolCalls": [ { "name": "GetRecentChanges", "arguments": { "component": "billing" } } ],
                "roundTrips": 2,
                "inputTokens": 1017,
                "outputTokens": 103,
                "cost": 0.0,
                "strippedFence": false
              }
            }
            """);
        SetResponseExample(operation, "400", "Empty title", """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": { "Title": [ "Title is required." ] }
            }
            """, "application/problem+json");
        SetResponseExample(operation, "502", "Provider down after retries", """{ "error": "The model provider is unavailable." }""");
        SetResponseExample(operation, "504", "Request timeout reached", """{ "error": "The request timed out." }""");

        return Task.CompletedTask;
    }

    private static OpenApiExample Example(string summary, string json) =>
        new() { Summary = summary, Value = JsonNode.Parse(json) };

    private static void SetResponseExample(OpenApiOperation operation, string status, string summary, string json, string contentType = "application/json")
    {
        if (operation.Responses?.TryGetValue(status, out var response) == true
            && response.Content?.TryGetValue(contentType, out var media) == true)
            media.Examples = new Dictionary<string, IOpenApiExample> { [summary] = Example(summary, json) };
    }
}
