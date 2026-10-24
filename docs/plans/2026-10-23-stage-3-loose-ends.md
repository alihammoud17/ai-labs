# Stage 3 loose ends: one source per fact, a ticket eval, two prompt fixes, a free Anthropic retry proof

- **Date:** 2026-10-23
- **Branch:** main (the user asked for everything on main)
- **Goal:** Close the six Stage 3 loose ends. Remove the two hand-kept duplicates (tool component list, test tickets). Add a repeatable, scored ticket eval. Measure two small prompt changes (no fences; don't blame an unrelated change) on Llama, then Haiku. Prove the Anthropic SDK's retries and timeout against a local fake server, for free.
- **Status:** done
- **Approval:** approved in plan mode (2026-10-23)

Claude writes the code (the user's request overrides tutor mode) and explains each change briefly. Steps marked **(you)** are the user's decisions. Commit after each part; push to `main` at the end.

## Steps

### Part 0: setup
- [x] **0.1** Save this plan to `docs/plans/2026-10-23-stage-3-loose-ends.md`. Commit.

### Part 1: stale branch wording
- [x] **1.1** `CLAUDE.md` Stage 3: "Branch `stage-3-tool-calling`" → "Built on branch `stage-3-tool-calling`, merged into `main`".
- [x] **1.2** Plan headers: every `**Branch:**` naming a feature branch becomes "`<branch>`, merged into main" (deliverable-3, stage-2, stage-2-loose-ends, stage-3). All their commits are on `main` already. Step text inside the plans (e.g. stage 3's 0.1 "create branch") stays: it's history of what was done.
- [x] **1.3** Commit.

### Part 2: one source for components and tickets
- [x] **2.1** **Tool description from the repository.** `TicketTools.AsAIFunction()` passes `AIFunctionFactoryOptions` whose `JsonSchemaCreateOptions.TransformSchemaNode` sets the `component` parameter's description to "The component to look up: one of {GetComponents()}." The hard-coded `[Description]` on the parameter goes. If that hook can't reach a parameter, fall back to a small `DelegatingAIFunction` that overrides `JsonSchema`. Check with a throwaway `check.cs` (scratchpad): the schema shows the four components; a fifth fake entry would show up without touching the tool.
- [x] **2.2** **Also a duplicate:** the system prompt in `TicketAnalyzer` hard-codes "auth, billing, invoices, tenants". It's built from `GetComponents()` too.
- [x] **2.3** **Tickets in one place:** `AiLab.Api/TestTickets/ticket-a.json` … `ticket-d.json` (request bodies only). **Not `tickets/`:** macOS's file system ignores case, so `tickets/` would be the existing code folder `Tickets/`. Copied to the output folder (`CopyToOutputDirectory`), since the content root is `bin/...`.
- [x] **2.4** `AnalyzeTicketExamples` reads the four files at startup (the summaries "A: explained by a billing change" etc. stay in code, keyed by letter). `analyze-ticket.http` uses `< ./TestTickets/ticket-a.json` as each body.
- [x] **2.5** Check: `/openapi/v1.json` has the four request examples with the same bodies as before; `/swagger` shows them in the dropdown; ticket C through the `.http` body gives 200 on Ollama. Commit.

### Part 3: the ticket eval
- [x] **3.1** `TestTickets/expected.json`: `{ "A": "4.18.0", "B": null, "C": "4.19.0", "D": null }`.
- [x] **3.2** **Shape: a Development-only endpoint `POST /dev/eval-tickets?runs=3&maxCost=0.03`.** Why: it runs inside the API, so every run goes through the exact same DI pipeline as `/analyze-ticket` (function invocation, iteration cap, logging, Polly/SDK retries) and reads the version list from `IChangeLogRepository`. A console tool would have to rebuild that DI setup or call the API over HTTP and duplicate the change log. Mapped only when `IsDevelopment()`, no request timeout (12 Haiku requests take minutes). Returns the report as `text/plain`, so `curl` prints it.
- [x] **3.3** `TicketAnalyzer` refactor: the model call and diagnostics move into `RunAsync`, which returns the raw reply text, the parsed analysis (or null) and the diagnostics, without throwing on a parse failure. `AnalyzeAsync` calls it and throws `ModelCallException` as today, so `/analyze-ticket` behaves the same.
- [x] **3.4** Scoring (`TicketScorer`), from `ProbableCause`, in this order (agreed with the user: the version-only rule would mark Haiku's "Unknown. … v4.17.1 … v4.16.0 …" as invented):
  1. says "unknown" (case-insensitive) → **unknown**
  2. contains the expected version → **right**
  3. contains any other version from the change log → **invented**
  4. no version → **unknown**

  Plus **failed** when the reply doesn't parse or the call ends in a 502. Correct = right when a version is expected, unknown when null.
- [x] **3.5** Output: one line per run (ticket, run, verdict, correct/wrong, tool calls with component, round trips, tokens in/out, cost, fenced), then totals per ticket (right/unknown/invented/failed, correct of N, fenced, cost) and overall. Stops early, with a note, if the summed cost passes `maxCost`.
- [x] **3.6** Raw replies: `eval-runs/<yyyyMMdd-HHmmss>_tickets_<model>/ticket-A-run1.json` (raw text, parsed analysis, diagnostics, verdict) plus `report.txt`, like the job eval.
- [x] **3.7** Test: Ollama, `runs=1`. Show the report. Commit.

### Part 4: baseline, then the prompt change
- [x] **4.1** Ollama baseline, current prompt, `runs=3` (12 requests, free).
- [x] **4.2** System prompt: add two lines, roughly
  - "Return only the JSON, with no code fences."
  - "The change log may not contain the cause. Only name a change if it can actually explain the symptom; otherwise say the cause is unknown and list what to check."

  Show the diff.
- [x] **4.3** Ollama again, new prompt, `runs=3`. Commit (prompt + nothing else).
- [x] **4.4 (you)** **STOP.** I show the Ollama before/after totals and ask for your Expected line for Haiku. I add it to `experiments.md` section 8 exactly as written and commit it before any Haiku call.
- [x] **4.5** Haiku, new prompt only, `runs=3` (12 requests, expected ≈ $0.007), in one API process started with `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5` and `maxCost=0.03`. Stop and tell you if Haiku spend in this task passes $0.03. `appsettings.json` stays on Ollama. Port 5028 is checked first, and only my own process is stopped.

### Part 5: Anthropic retries and timeout, against a fake server (no real API call)
- [x] **5.1** Base URL: the SDK has `AnthropicClient.BaseUrl` and mentions `ANTHROPIC_BASE_URL`. First test whether the env var alone redirects our factory's client. If it does, no code change. If not, add an optional `anthropicBaseUrl` to `ChatClientFactory.Create` and `Chat:AnthropicBaseUrl` config (null = SDK default), like `Chat:OllamaUrl`.
- [x] **5.2** Fake server: a throwaway Python script in the scratchpad (not committed). It logs each request with a timestamp and a running count. Mode `529` answers 529 with an Anthropic-style error body; mode `slow` sleeps 10 s before answering. The API runs with `Anthropic__ApiKey=fake-key`, so the real key never leaves the machine.
- [x] **5.3** Check (a) retries: mode 529, ticket A. Expect 3 hits (1 + `MaxRetries` 2) with growing gaps (the SDK's backoff), one `GetResponseAsync` in our log, then 502 "The model provider is unavailable."
- [x] **5.4** Check (b) timeout: mode `slow`, `Resilience__AttemptTimeoutSeconds=2`. Expect 3 hits ~2 s plus backoff apart, then 502.
- [x] **5.5** Check (c): the server's count is exactly 3 per request in both checks, so nothing retries on top of the SDK.
- [x] **5.6** If 5.1 needed code, commit it. Record the timings for Part 6.

### Part 6: write it up
- [x] **6.1** `experiments.md` section 8, "Stage 3 follow-ups", plain first-person tone: the Expected line; a table per ticket and in total (Ollama before, Ollama after, Haiku after: right / unknown / invented over 3 runs, fenced replies, cost); did "no fences" fix Haiku; did the new instruction reduce Llama's inventions; the fake-server proof; a short conclusion.
- [x] **6.2** This plan's Changes log and Status; `CLAUDE.md` (eval endpoint, `TestTickets/`, current step); `README.md` ("What I learned" if it changes, total spend). Commit and push to `main`.

## Tests
Manual, Ollama unless stated, API started from the repo root with `dotnet run --project AiLab.Api` (port 5028 checked first):
- Part 2: schema shows the components from the repository; Swagger shows the four examples; `.http` file bodies load; ticket C → 200.
- Part 3: `runs=1` report has 4 lines and totals; files land in `eval-runs/`; `/analyze-ticket` still returns 200/400/502 as before; `/dev/eval-tickets` is 404 outside Development.
- Part 4: Haiku spend ≤ $0.03.
- Part 5: server-side hit counts and timestamps, our log, and the 502 bodies.

## Decisions & trade-offs
- **Dev endpoint over a console tool** (3.2): same pipeline, no duplicated DI.
- **"unknown" checked first** (3.4): a cause that lists versions to rule them out isn't an invention.
- **`TestTickets/`, not `tickets/`** (2.3): avoids a case clash with `Tickets/` on macOS.
- **Env var before new code** (5.1): only add a config option if the SDK doesn't already honour `ANTHROPIC_BASE_URL`.

## Out of scope
- A test project; changing `TicketAnalysis`'s schema rules; more tickets; resilience changes.

## Changes
- **2026-10-23:** Part 1 done. Only the five headers changed; step text in older plans stays as history.
- **2026-10-23:** Part 2 done.
  - `AIJsonSchemaCreateOptions.ParameterDescriptionProvider` sets the `component` description from `GetComponents()`, so no `DelegatingAIFunction` was needed. A throwaway check showed the same text as before and a fifth fake component appearing without touching the tool.
  - The tickets live in `TestTickets/`, read by `Evaluation/TestTicketFiles`. The four Swagger example bodies are identical to the old ones (compared as JSON).
- **2026-10-23:** Part 3 done.
  - **Bug found:** `dotnet run --project` starts the API in `AiLab.Api/`, so the first run saved its replies to `AiLab.Api/eval-runs/`. The eval now finds the repo root (the folder with `AiLab.slnx`), and the run was moved.
  - **Scorer checked offline** on the 8 saved Stage 3 replies: it matches the hand scores in section 7, except Llama's A, which has no version and so scores "unknown", as agreed.
  - **Endpoint checks:** `runs=0` gives 400, it's 404 in Production, and `/analyze-ticket` still gives 200 and 400.
- **2026-10-23:** Part 4 done.
  - **Ollama:** 5/12 correct before the change and 6/12 after. Inventions on B and D went from 6 to 3 by score, or from 6 to 4 by hand: Llama often drops the version, which the scorer can't see.
  - **Haiku:** the user's Expected line was committed (`c251072`) before any Haiku call. Haiku scored 12/12, with 0 fenced replies, for $0.006963. That's the only Haiku spend in this task.
- **2026-10-23:** Part 5 done, with no code change: the SDK reads `ANTHROPIC_BASE_URL`, and the factory needed no option.
  - **Setup:** a fake Python server in the scratchpad; `Anthropic__ApiKey=fake-key`.
  - **529 on every call:** 3 hits per request (retry-count 0, 1, 2), 0.44 s and 0.83 s apart, then a 502 after 1.3 s.
  - **10 s delay with a 2 s attempt timeout:** 3 hits per request, then a 502 after 7.35 s. Our log shows one `GetResponseAsync` per request.
  - **Unexplained:** the first request in the first 529 check reached the server about 6 s late. It didn't happen again.
- **2026-10-23:** Part 6 done.
  - `experiments.md` has a new section 8.
  - `CLAUDE.md` has the stack notes and a loose-ends list.
  - `README.md` has the ticket eval, the project structure, "What I learned" and the total spend (about $0.036).
