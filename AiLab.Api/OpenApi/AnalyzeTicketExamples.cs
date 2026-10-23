using AiLab.Api.Evaluation;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace AiLab.Api.OpenApi;

// Examples shown in Swagger UI for POST /analyze-ticket. The request examples are the test tickets,
// read from TestTickets/; the 200 example is a real Ollama reply to ticket C (2026-10-23, after the defensive-UX changes).
public static class AnalyzeTicketExamples
{
    // Only the dropdown labels live here; the bodies come from the files
    private static readonly Dictionary<string, string> Summaries = new()
    {
        ["A"] = "A: explained by a billing change",
        ["B"] = "B: no change explains it",
        ["C"] = "C: no component given",
        ["D"] = "D: vague, no change explains it",
    };

    public static Task Apply(OpenApiOperation operation, CancellationToken _)
    {
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var body) == true)
        {
            body.Examples = TestTicketFiles.ReadAll().ToDictionary(
                t => t.Letter,
                t => (IOpenApiExample)Example(Summaries.GetValueOrDefault(t.Letter, t.Letter), t.Json));
        }

        SetResponseExample(operation, "200", "Ticket C on llama3.2",
            """
            {
              "notice": "AI-generated analysis. Verify before acting. A grounded cause cites a real recent change, but may still not explain the problem.",
              "aiAnalysis": {
                "summary": "numbering is messed up",
                "probableCause": "Per-tenant invoice numbering (INV-<TENANTCODE>-0001), replaces the global sequence.",
                "causeVersion": "4.19.0",
                "testCases": [
                  "Given a multi-tenant SaaS, when a new invoice is generated, then the invoice number should be in the format INV-<TENANTCODE>-0001.",
                  "Given a multi-tenant SaaS with a tenant that had its VAT number changed, when a new invoice is generated, then the VAT number should be shown in the invoice PDF footer.",
                  "Given a multi-tenant SaaS, when the per-tenant numbering feature is disabled, then the invoice number should be in the global sequence.",
                  "Given a multi-tenant SaaS, when the per-tenant numbering feature is enabled but the tenant code is incorrect, then the invoice number should be incorrect."
                ]
              },
              "verification": {
                "causeStatus": "grounded",
                "reason": "Cites 4.19.0, which GetRecentChanges returned for invoices in this request.",
                "evidence": [
                  {
                    "component": "invoices",
                    "version": "4.19.0",
                    "changedAt": "2026-10-21T18:00:00+03:00",
                    "description": "Per-tenant invoice numbering (INV-<TENANTCODE>-0001), replaces the global sequence."
                  }
                ],
                "toolCalled": true,
                "toolFailed": false
              },
              "diagnostics": {
                "model": "llama3.2",
                "toolCalls": [ { "name": "GetRecentChanges", "arguments": { "component": "invoices" } } ],
                "roundTrips": 2,
                "inputTokens": 1397,
                "outputTokens": 232,
                "cost": 0.0,
                "strippedFence": false,
                "repaired": false
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
        SetResponseExample(operation, "422", "Not a support ticket", """{ "error": "This doesn't look like a support ticket.", "code": "not_a_ticket" }""");
        SetResponseExample(operation, "502", "Provider down after retries", """{ "error": "The model provider is unavailable.", "code": "provider_unavailable" }""");
        SetResponseExample(operation, "504", "Request timeout reached", """{ "error": "The request timed out.", "code": "timeout" }""");
        SetResponseExample(operation, "500", "A bug of ours", """{ "error": "Something went wrong.", "code": "internal_error" }""");

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
