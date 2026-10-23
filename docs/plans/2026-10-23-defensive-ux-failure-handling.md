# Defensive UX and clean failure handling for POST /analyze-ticket, plus integration tests

- **Date:** 2026-10-23
- **Branch:** main (the user asked for everything on main)
- **Goal:** Close the "bad answers return 200 and nobody notices" gap from `docs/failure-table.md`. The server checks the model's cause against what the tool really returned, keeps the AI's guess apart from server facts, fails cleanly on cut-off / unreadable / non-ticket replies, and survives a failing tool. All of it is proven by offline integration tests, with no network, no Ollama and no key.
- **Status:** in progress
- **Approval:** approved in plan mode (2026-10-23)

Claude writes the code (the user's request overrides tutor mode) and explains each change briefly. Steps marked **(you)** are the user's decisions. Commit after each part and push to `main`. No Haiku calls in this task.

## Context

The failure table counts 15 rows that nothing detects. The worst are 12–14: a made-up cause, an answer without the tool, and empty fields. All three return 200. Sections 7–8 showed what this looks like on Llama:
- It blamed the real but irrelevant v4.17.1 on B and D.
- It often copied a change's text without its version.

This work applies the four defensive UX patterns:
- **Attribution:** server-built evidence and a `causeStatus`.
- **Set expectations:** a fixed notice, and an honest Swagger text.
- **Efficient dismissal:** the guess and the facts are separate, so a client can drop the guess. Errors carry stable codes.
- **Anchor on familiarity:** Given/When/Then test cases, and the summary as a one-line bug report.

**One limit, stated up front:** the grounding check proves that a cited version *came from the tool in this request*. It cannot prove that the change *explains the symptom*. Llama's B/D answer (v4.17.1, which the tool really returned) will still be **Grounded**. "Grounded" means "uses real evidence", not "correct". The notice and the Swagger text say so. Row 12 is only half closed: invented versions and unversioned causes are caught, but wrong use of a real change isn't.

## Proposed order (one change from the brief)

The test harness (keyed provider client, scripted fake, test project) comes first, as Part 0. Each later part then adds its own tests from the Part 6 list, instead of all tests at the end. Why: every part gets proven offline as it lands, and Part 6 shrinks to the scorer fix plus a final run. Say so if you'd rather keep the original order.

## Steps

### Part 0: setup and test harness (no behaviour change)
- [ ] **0.1** Save this plan to `docs/plans/2026-10-23-defensive-ux-failure-handling.md`. Commit.
- [ ] **0.2** **One pipeline for app and tests.** In `Program.cs`, the provider client becomes a keyed singleton, `AddKeyedSingleton<IChatClient>("provider", …)`, which calls `ChatClientFactory.Create` as today. `AddChatClient(sp => sp.GetRequiredKeyedService<IChatClient>("provider")).UseFunctionInvocation(…).UseLogging()` stays the only place the pipeline is built. Tests replace only the `"provider"` key, so the **real** function-invocation and logging layers run above the fake. Nothing is duplicated, so the pipeline can't drift between app and tests. Add `public partial class Program;` for `WebApplicationFactory<Program>`.
- [ ] **0.3** New project `AiLab.Api.Tests` (xUnit v3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.x, both already in the NuGet cache), added to `AiLab.slnx`:
  - **`ScriptedChatClient`:** an `IChatClient` that returns queued `ChatResponse`s in order. It records a copy of each call's messages and options, and fails the test if it's called more often than scripted. Helpers: `ToolCall("billing")`, `Reply(json, finishReason = Stop)`, fixed `Usage`.
  - **`ApiFactory`:** a `WebApplicationFactory<Program>` with environment `Testing`, so no user-secrets and no key are needed. It replaces the `"provider"` key with the script.
  - Check that `TestTickets/*.json` reaches the test output folder (the Swagger examples read it at startup). If it doesn't, add a `None` link in the test csproj.
- [ ] **0.4** First tests, against today's code: **#9** empty title → 400, and a baseline happy path → 200 (rewritten in Part 1). `dotnet test` passes offline. Commit and push.

### Part 1: grounding check (attribution), rows 12 and 13
- [ ] **1.0 (you)** **STOP.** Show the exact response JSON and the `causeStatus` rules (draft below). Approve or change them.
- [ ] **1.1** **What the tool returned.** `TicketTools` gets `ParseChanges(string toolResult)`, right next to the formatter it reverses (`yyyy-MM-dd HH:mm zzz v<version>: <description>`). The grounding step pairs each `FunctionCallContent` (component) with its `FunctionResultContent` by `CallId`. A result counts only when:
  - `Exception` is null, and
  - it isn't the "Unknown component" message.

  The `ChangeEntry`s parsed from those results are the **only** source for evidence. The repository isn't asked again, so evidence is what the model actually saw.
- [ ] **1.2** **A model field for the claim:** `TicketAnalysis.CauseVersion` (string?): "The version of the one change you name as the cause, exactly as GetRecentChanges returned it, or null if the cause is unknown." Why a field and not only a regex over `ProbableCause`: Haiku writes "Unknown. … v4.17.1 … v4.16.0 … neither explains it". Its text mentions versions it is ruling out, so only an explicit claim says which version is *the cause*. The model writes the claim and the server checks it.
- [ ] **1.3** `Grounding/CauseCheck.Evaluate(analysis, toolResults)` sets `CauseStatus`, a reason sentence, and evidence. The rules come below the plan's steps (see "causeStatus rules").
- [ ] **1.4** **Unverified still returns 200, with the model's text unchanged.** The client sees `causeStatus: "unverified"` and a reason, and can hide the guess. Trade-off:
  - Rewriting the cause to "unknown" would hide information that is sometimes right. Llama copied the *right* change's text without its version on A and C.
  - It would make the eval blind to what the model said.
  - It would put server text in a field the client reads as AI output.

  Cost of keeping it: a careless client that ignores `causeStatus` still shows the guess. The separated shape (Part 2) and the notice limit that risk.
- [ ] **1.5** Tests **#1** (happy path: grounded, evidence equals the repository's 4.18.0 entry, the tool really ran), **#2** (tool returned 4.18.0, model claims 4.17.1 → unverified), **#3** (no tool call, cause named → unverified). Plus a test for the dropped version: Llama-style text with no `causeVersion` → unverified. Commit and push.

### Part 2: set expectations and efficient dismissal, rows 12, 13 and 16 (mitigation)
- [ ] **2.1** Response shape (draft below): `notice`, `aiAnalysis` (everything the model wrote), `verification` (everything the server computed), `diagnostics`. Enums are serialized as camelCase strings (`JsonStringEnumConverter` in `ConfigureHttpJsonOptions`).
- [ ] **2.2** Notice, server text, the same on every 200: "AI-generated analysis. Verify before acting. A grounded cause cites a real recent change, but may still not explain the problem."
- [ ] **2.3** Swagger description rewritten in plain terms.
  - **Can:** summarize, suggest a cause from this component's recent changes, and draft test cases.
  - **Can't:** see code, logs or tenant data. It can't confirm that a change explains the problem, and it may say "unknown".
  - **Also:** what each `causeStatus` means, and the error codes.

  Update the 200 example (a real Ollama reply from Part 6) and add 422 examples.
- [ ] **2.4** Tests: #1 extended to assert the shape and the notice. Commit and push.

### Part 3: anchor on familiarity (descriptions only)
- [ ] **3.1** `TicketAnalysis` descriptions:
  - **Summary:** "One line, as a bug report title: what fails, for whom, since when if known (e.g. 'Pro upgrade charged 1 cent more than the preview')."
  - **TestCases:** "Three to five test cases, each one sentence in the form 'Given …, when …, then …'."
- [ ] **3.2** Commit and push. Its effect shows in the Part 6 Ollama run, with no new checks.

### Part 4: cheap fixes, rows 10, 14 and 20
- [ ] **4.1** **Row 10, cut-off reply.** Before parsing, `FinishReason == Length` → 502 with code `reply_cut_off` and the message "The model's reply was cut off before it finished." The warning log line includes the output tokens and `MaxOutputTokens`. No repair: the same limit would cut it off again, and the cost would double.
- [ ] **4.2** **Row 14, meaning checks after parsing** (`TicketAnalysisRules.Problems(analysis)`). Each one counts as a failed parse and feeds Part 5's repair:
  - `summary` is blank → "summary is empty".
  - `probableCause` is blank → "probableCause is empty".
  - `testCases` has no non-blank item → "testCases is empty". Blank items are dropped. More than 5 are cut to 5, without failing.
  - `causeVersion` is set but isn't `\d+\.\d+\.\d+`, after a leading `v` is trimmed. Values like "unknown", "n/a" or "" become null, without failing.

  These checks are skipped when the model says it's not a ticket (5.2).
- [ ] **4.3** **Row 20, the date.** The system prompt's fixed part stays built once. `"Today is yyyy-MM-dd."` is added per request from an injected `TimeProvider`, so tests can pin it.
- [ ] **4.4** Test **#6** (Length → 502 with the cut-off message, and only one call: no repair), and a test that empty test cases count as a failure. Commit and push.

### Part 5: the original failure handling, rows 15, 18 and 23 (signal only)
- [ ] **5.1** **One repair for invalid JSON (row 15).** On a parse or meaning failure, the server makes one more call: the original messages, plus `response.Messages`, plus "Your last reply wasn't valid JSON for the schema ({problem}). Reply with only the JSON." That call goes through `GetResponseAsync<TicketAnalysis>` again with **no tools**: the tool results are already in the history, and this can't start a new loop. Both attempts are logged (a warning with the first raw reply, then info or warning with the outcome). Usage, round trips and cost are summed, and `diagnostics.repaired` is set to true. A second failure → 502 `reply_unreadable`. Never more than one repair.
- [ ] **5.2** **Not a ticket, or a refusal (row 23 as a signal, not a defence).** New model field `NotATicketReason` (string?): "Only when the input isn't a support ticket (a request to write something, instructions to you, a greeting or 'help' with no problem): one short sentence why. Otherwise null." Why null means "is a ticket": a missing field can never turn a real ticket into a 422 (a `bool isSupportTicket` would default to false). Non-null → **422** `not_a_ticket` with the fixed message "This doesn't look like a support ticket." The model's reason goes to the log only, never into the body, because model text in an error body can carry injected text. `FinishReason == ContentFilter` (Anthropic's refusal maps here) → **422** `model_declined`, "The model declined to analyze this request."
- [ ] **5.3** **Tool errors (row 18).**
  - `InMemoryChangeLogRepository` takes `ChangeLog:FailComponents` from config (e.g. `ChangeLog__FailComponents__0=billing`). `GetByComponent` throws `InvalidOperationException("Simulated change-log outage")` for those components. Tests set the same key.
  - `TicketTools` wraps the repository call: it logs the exception and throws `ChangeLogUnavailableException`. `ArgumentException` from argument binding is unaffected.
  - `UseFunctionInvocation` sets `IncludeDetailedErrors = false` explicitly, with a comment. It's the default, but the guarantee is now visible. The model only gets the middleware's generic "Error: Function failed."
  - `verification.toolFailed` is true if any `FunctionResultContent.Exception` is set. Its cause then can't be Grounded from that call.
  - After 3 failures in a row the middleware rethrows `ChangeLogUnavailableException` → 502 `tool_failed`, no longer a 500.
- [ ] **5.4** **Consistent error bodies.** `ErrorResponse(string Error, string Code)` for 422, 502, 504 and 500:
  - **Codes:** `provider_unavailable`, `tool_failed`, `reply_cut_off`, `reply_unreadable`, `not_a_ticket`, `model_declined`, `timeout`, `internal_error`.
  - The exception handler writes `{ error: "Something went wrong.", code: "internal_error" }`.
  - 400 stays ASP.NET's validation problem: its `errors` map per field is the standard, and Swagger already documents it.
  - No stack trace or exception message reaches a body.
- [ ] **5.5** Tests **#4** (invalid then valid → 200, `repaired`, 2 calls), **#5** (invalid twice → 502 `reply_unreadable`, exactly 2 calls), **#7** (`notATicketReason` set → 422), a ContentFilter → 422 test, and **#8** (tool throws → 200, `toolFailed`, and no "Simulated" text in any message the fake received). Commit and push.

### Part 6: scorer fix, full run, write-up
- [ ] **6.1** **`TicketScorer` order:** expected version first, then "unknown", then other versions → invented, then no version → unknown. Unit tests cover each branch:
  - "Unknown, maybe 4.18.0" for A → right.
  - Haiku's "Unknown. … 4.17.1 … neither" for B → unknown.
  - "4.17.1" for B → invented.
  - Text only → unknown.
  - "14.18.0" doesn't match 4.18.0.

  The eval line also shows `causeStatus`, and `TicketEval` handles the new outcomes (422 and 502 → failed, with the code).
- [ ] **6.2** Run `dotnet test`: every test passes, and the output is shown.
- [ ] **6.3** Ollama, the four tickets once each: `POST /dev/eval-tickets?runs=1`, port 5028 checked first, only my own process stopped. Show `causeStatus`, the reason and the evidence for A–D.
- [ ] **6.4** `docs/failure-table.md`: a new "Handled by" column for the rows this closes. "Partly" for row 12 (real-but-wrong change) and row 23 (signal only).
- [ ] **6.5** `experiments.md` section 9, "Defensive UX", in the same first-person tone: what each pattern became, the Ollama results for A–D, and what the grounding check caught and missed.
- [ ] **6.6** This plan's Changes log and Status; `CLAUDE.md` (stack, tests, current step); `README.md`. Commit and push.

## Response shape (draft, confirmed at 1.0)

```json
{
  "notice": "AI-generated analysis. Verify before acting. A grounded cause cites a real recent change, but may still not explain the problem.",
  "aiAnalysis": {
    "summary": "Pro upgrade charged 1 cent more than the preview",
    "probableCause": "v4.18.0 changed proration to round up, and the plan-change preview wasn't updated.",
    "causeVersion": "4.18.0",
    "testCases": ["Given a Starter tenant mid-month, when it upgrades to Pro, then the charge equals the preview."]
  },
  "verification": {
    "causeStatus": "grounded",
    "reason": "Cites 4.18.0, which GetRecentChanges returned for billing in this request.",
    "evidence": [
      { "component": "billing", "version": "4.18.0", "changedAt": "2026-10-19T09:15:00+03:00",
        "description": "Proration now rounds up to the nearest cent instead of banker's rounding. Plan-change preview not updated yet, follow-up to come." }
    ],
    "toolCalled": true,
    "toolFailed": false
  },
  "diagnostics": {
    "model": "llama3.2", "toolCalls": [ { "name": "GetRecentChanges", "arguments": { "component": "billing" } } ],
    "roundTrips": 2, "inputTokens": 1017, "outputTokens": 103, "cost": 0.0, "strippedFence": false, "repaired": false
  }
}
```

`notATicketReason` is a model field, but it never appears in a 200: it either is null, or the request ends in a 422.

## causeStatus rules (draft, confirmed at 1.0)

**Inputs:**
- `returned`: the versions parsed from this request's successful tool results.
- `claimed`: `causeVersion` (normalized). If it's null and the text doesn't say "unknown", the versions found in `probableCause`.
- `mentioned`: every `x.y.z` found in `probableCause`.
- `saysUnknown`: `\bunknown\b` in `probableCause`.

**Rules, first match wins:**
1. Some version in `claimed` or `mentioned` isn't in `returned` → **unverified**. Reason: "Cites 4.17.1, which the tool didn't return in this request" (or "the tool wasn't called / failed"). Mentioning a version that doesn't exist is a hallucination, even when it's being ruled out.
2. `claimed` is empty and `saysUnknown` → **unknown**. Evidence stays empty.
3. `claimed` is non-empty and `saysUnknown` → **unverified**: "The cause says unknown but also cites 4.18.0."
4. `claimed` is non-empty (so every version is in `returned`, by rule 1) → **grounded**. Evidence = the parsed entries for `claimed`.
5. Otherwise, a cause is named with no version → **unverified**: "Names a cause without citing a change version." Detection is simply "not unknown, and no version anywhere". If the text contains a 6-word run from a returned description, the reason adds "(its text matches 4.18.0)". It stays unverified. Section 8's D run 1 copied the text of the *wrong* change, so a text match shows where the words came from, not that they're right.

`toolFailed` doesn't change the rules. A failed call returns nothing, so anything that relies on it fails rule 1.

## Failure-table rows covered

| Row | Failure | Part | After |
|---|---|---|---|
| 10 | Output truncated | 4.1 | 502 `reply_cut_off` + its own log line |
| 12 | Made-up cause | 1, 2 | Invented and unversioned causes → `unverified`. A real-but-wrong change is still `grounded` (partly closed) |
| 13 | Answer without the tool | 1 | `toolCalled: false` plus `unverified` for any named cause |
| 14 | Valid JSON, missing fields | 4.2, 5.1 | Counts as a parse failure → one repair → 502 |
| 15 | Wrong shape | 5.1 | One repair, then 502 |
| 16 | Different answer each time | 2 | Not fixed. Notice and status let a client discount it |
| 18 | Real repository throws | 5.3 | Model continues, `toolFailed`, 502 `tool_failed` instead of 500 |
| 20 | Model doesn't know the date | 4.3 | Date in the prompt |
| 23 | Prompt injection | 5.2 | Partly: a 422 signal when the model notices, not a defence |

## Tests

`dotnet test` offline (no network, Ollama or key). The brief's tests are #1–9, plus the dropped-version, empty-test-cases, ContentFilter and scorer tests. Manual: the Part 6 Ollama run. Swagger shows the new shape and examples.

## Decisions & trade-offs
- **Keyed provider client** (0.2): one pipeline, with the fake strictly below the real middleware.
- **Evidence from parsed tool results, not the repository** (1.1): evidence is what the model saw.
- **`causeVersion` field plus text scan** (1.2): an explicit claim, still checked against the free text.
- **Unverified stays 200, unchanged** (1.4): transparency over hiding. The status does the warning.
- **No repair on Length** (4.1): it would be cut off again, at double the cost.
- **Repair without tools** (5.1): no new tool loop, and the history already holds the evidence.
- **`notATicketReason` null = ticket** (5.2): a missing field never causes a false 422. Model text never reaches an error body.
- **400 keeps the validation problem** (5.4): the field map is useful and standard. Every other error is `{error, code}`.

## Out of scope
- Rows 21 (component validation), 22 (auth), 24 (input size), 25 (diagnostics exposure), metrics and health checks.
- Judging whether a real change explains the symptom (would need a second model call or rules).
- Haiku runs.

## Changes
- **2026-10-23:** Part 0 done. The provider client is keyed `"provider"`, and the test factory replaces only that key. `TestTickets/` reaches the test output folder by itself, so the test csproj needed no link.
- **2026-10-23:** The user approved the JSON shape and the `causeStatus` rules at step 1.0, unchanged.
- **2026-10-23:** Part 1 done.
  - Evidence is built from `ChangeEntry`s that `TicketTools.ParseChanges` reads back from the tool results. A round-trip test proves it reads exactly what the repository returned, for all four components.
  - The response gets a `verification` object next to `analysis`. The rename to `aiAnalysis` and the notice come in Part 2.
  - Enums are serialized as camelCase strings, in the API and in the eval's saved files.
  - 11 tests pass offline.
- **2026-10-23:** Part 2 done.
  - The response is `notice`, `aiAnalysis`, `verification`, `diagnostics`, in that order. Test #1 asserts the order and the notice.
  - The Swagger description says what the endpoint can and can't do, and what each status means. `CauseStatus` shows in the OpenAPI document as `grounded / unknown / unverified`.
  - The 200 example is ticket A's earlier Ollama reply in the new shape. It gets replaced by a real reply in Part 6. The 422 examples come with the 422s in Part 5.
- **2026-10-23:** Part 3 done. Descriptions only.
- **2026-10-23:** Part 4 done.
  - `RunAsync` returns a `RunOutcome` (`Analyzed`, `CutOff`, `Unreadable`) instead of a nullable analysis. `AnalyzeAsync` maps it to the 502 messages.
  - The meaning checks are in `TicketAnalysisRules.Check`, which also hands back the cleaned analysis (blank test cases dropped, at most 5, `causeVersion` normalized).
  - The date comes from a `TimeProvider` registered in `Program.cs`. The test pins it with a `FixedTimeProvider`.
  - 14 tests pass.
- **2026-10-23:** Part 5 done.
  - **The repair call keeps the tool defined**, with `ToolMode = None`, instead of "no tools" as planned. Anthropic rejects a history with tool calls when no tools are defined. The test checks `ToolMode.None` on the repair call.
  - **Row 18 behaves better than planned.** A tool failing every time doesn't end in a 502. The cap (3 calls with tools) ends the loop before the middleware's rethrow (more than 3 errors in a row), so the model gets its final call without tools and answers. The result is a 200 with `toolFailed: true` and an unknown cause. The `tool_failed` 502 is kept for errors that do pile up, for example several calls in one step.
  - **`notATicketReason` as text** ("null", "none", "n/a", "no") means "a ticket" and is cleared, so it never shows in a 200. The first test run caught this leaking.
  - **The 500 handler writes `{error, code}`.** A body that can't be read keeps its 400, with `invalid_request`. `AddProblemDetails` is gone, since nothing uses it any more.
  - 22 tests pass.
