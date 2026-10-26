# ai-lab

A small .NET lab I built to learn how LLM integration works in practice: streaming, conversation history, token costs, logging, typed output, how much a better prompt is really worth, and tool calling inside an ASP.NET Core API.

It isn't a product. It's a lab bench. You change one thing, run it again, and write down what changed. The results are in [`experiments.md`](experiments.md).

## What it does

- **Chats in the terminal** with a local model (Llama 3.2 through Ollama, free) or with Claude Haiku 5.5 through the Anthropic API.
- **Streams replies** as they're generated, instead of waiting for the full answer.
- **Remembers the conversation.** The whole history is sent with every message, which is how the model "remembers" anything.
- **Shows what each turn cost**, with input and output tokens, the finish reason, the turn's cost, and a running total for the session:

  ```
  Tokens: in=123, out=93 | Finish Reason: stop | Turn: $0.000059 | Session: $0.000108
  ```

- **Warns when an answer was cut off.** If the model hits the output limit, you see `Warning: answer was cut off`. Without that warning, a truncated answer looks like a normal one.
- **Extracts job postings into a typed C# record** with `/extract`. It turns a pasted LinkedIn post into a `JobPosting` (title, company, location, work mode, skills, years of experience) and scores it against a hand-written answer.
- **Logs every model call to a file**, so you can see exactly what was sent (messages, options, schema) without cluttering the chat.

- **Analyzes support tickets over HTTP** (`AiLab.Api`). The model reads a fake change log through a tool and returns a summary, a probable cause and test cases. See [The ticket API](#the-ticket-api).

All the app code talks to `IChatClient` from `Microsoft.Extensions.AI`. The client is registered once in DI, so switching between Ollama and Claude is a config change. Nothing else in the code knows which provider it's using.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) with the Llama 3.2 model, for free local use:

  ```bash
  ollama pull llama3.2
  ```

- The `nomic-embed-text` embedding model, for stage 4 (embeddings and RAG):

  ```bash
  ollama pull nomic-embed-text
  ```

- An Anthropic API key, only if you want to use Claude.

## Getting started

```bash
git clone https://github.com/alihammoud17/ai-labs.git
cd ai-labs
dotnet run --project AiLab.Console
```

By default it uses Ollama, so make sure Ollama is running first. Run it from the repo root, so `/extract inputs/job-1.txt` and the `prompts/` folder are found.

### Using Claude instead

1. Store your API key with user-secrets. It never goes in code or config files.

   ```bash
   cd AiLab.Console
   dotnet user-secrets set "Anthropic:ApiKey" "your-key-here"
   ```

2. Switch the provider in `AiLab.Console/appsettings.json`:

   ```json
   {
     "Chat": {
       "Provider": "anthropic",
       "Model": "claude-haiku-5-5"
     }
   }
   ```

User-secrets are only loaded in the Development environment. `dotnet run` uses the launch profile in `Properties/launchSettings.json`, which sets `DOTNET_ENVIRONMENT=Development` for you. Without it, the key is missing and the app stops with "Anthropic:ApiKey is missing".

Switch the provider back to `ollama` / `llama3.2` when you're done, so day-to-day testing stays free.

### Embeddings

Embeddings have their own section in `AiLab.Console/appsettings.json`, separate from `Chat`:

```json
{
  "Embedding": {
    "Provider": "ollama",
    "Model": "nomic-embed-text",
    "OllamaUrl": "http://localhost:11434"
  }
}
```

They're separate because chat can run on Claude, but Anthropic has no embeddings API. Only `ollama` is supported. `anthropic`, any other provider, or a missing provider or model stops the app with a message that names the key to set. The model decides the vector size (`nomic-embed-text` gives 768 numbers, `all-minilm` 384), so switching models is a config change. `OllamaUrl` is optional and defaults to `http://localhost:11434`.

The generator is created the first time something asks for it, so the API runs without an `Embedding` section.

## Using the app

| Input | What it does |
|---|---|
| any text | Sends a message |
| `/extract [v1\|v2] <path>` | Extracts a job posting into a typed record, using prompt `v1` (default) or `v2`, and scores it if a golden answer exists. Doesn't touch the chat history |
| `/eval <v1\|v2> [runs]` | Runs the prompt `runs` times (default 3) on every `inputs/job-N.txt` that has a golden answer. It prints a scored line per run, then totals: parse failures, fields and skills recall (average, lowest, highest per pass), tokens and cost. Raw replies are saved to `eval-runs/` (not committed). Doesn't touch the chat history |
| `/reset` | Starts a fresh conversation (keeps the system message) |
| `/exit` | Quits |
| Ctrl+C while a reply is streaming | Stops that reply only, and discards the turn |
| Ctrl+C during `/extract` or `/eval` | Stops it. `/eval` still prints the totals so far |
| Ctrl+C at the prompt | Quits |

An `/extract` run looks like this:

```
You: /extract v2 inputs/job-2.txt
[extracting with prompt v2...]
Title:             C# Developer - Core Trading Technology
Company:           Susquehanna International Group
Location:          Sydney, Australia
Work mode:         OnSite
Required skills:   C#, HTML, JavaScript (3)
Years experience:  4
Tokens: in=2893, out=479 | Finish Reason: stop | Turn: $0.000529 | Session: $0.002573
Score vs inputs/job-2.expected.json:
  ok     title
  ...
Fields: 5 of 5 exact
Skills recall: matched 3 of 4
```

An `/eval` run prints one line per extraction and the totals at the end:

```
You: /eval v2 3
[eval v2: 5 inputs × 3 runs on claude-haiku-5-5, replies saved to /Users/you/ai-labs/eval-runs/20261022-120444_claude-haiku-5-5_v2/]
job-1 run 1  parsed  fields 5/5  skills 10 of 12  in=4565 out=750  $0.000832
...
Totals for prompt v2:
  Parse failures: 0 of 15 runs
  Pass 1: fields 25/25, skills 36 of 44
  ...
  Fields correct: avg 25.0 of 25, lowest 25, highest 25 (over 3 of 3 passes)
  Skills recall:  avg 36.0 of 44, lowest 36, highest 36
  Tokens: in=51207, out=9874 | Cost: $0.010058 | Session: $0.019594
```

### Logs

Model calls are logged by the `UseLogging()` middleware, through Serilog, to a daily file next to the app: `AiLab.Console/bin/Debug/net10.0/logs/ai-YYYYMMDD.log`. The app prints the folder at startup.

At the default `Debug` level you get one line when a call starts and one when it ends. To see the full request (every message, the options and the JSON schema), set the level to `Verbose`. Either change `Serilog:MinimumLevel:Override` in `appsettings.json`, or run:

```bash
env 'Serilog__MinimumLevel__Override__Microsoft.Extensions.AI=Verbose' dotnet run --project AiLab.Console
```

(`env` is needed because the setting name contains dots, which a shell variable name can't.)

### Settings worth knowing

These live in `AiLab.Application/Services/ChatExperimentSettings.cs` and are meant to be edited between experiments:

- **`CreateOptions()`** returns the `ChatOptions` sent with every turn: temperature, max output tokens and similar. Empty means "use the provider's defaults".
- **`JsonSystemMessage`** is the format-forcing system message from experiment 4. It's off (`null`) by default; set it to the string in the comment to turn it on.
- **`MaxHistoryMessages`** (20) is a simple cap on history length. The oldest exchange is dropped first, and system messages are never trimmed.

## The ticket API

`AiLab.Api` is a minimal ASP.NET Core API with one endpoint, `POST /analyze-ticket`. You send a support ticket for a multi-tenant SaaS (title, description, optionally the component: auth, billing, invoices or tenants). The model gets one tool, `GetRecentChanges(component)`, which reads a fake change log. The function-invocation middleware runs the tool calls. The reply comes back as a typed `TicketAnalysis`:

- **summary:** one line, like a bug report title
- **probable cause:** must name a change from the change log, or say the cause is unknown, plus **causeVersion**, the version it names
- **test cases:** in Given/When/Then form

The server then checks the cause against what the tool really returned in that request (see [Reading the response](#reading-the-response)).

### Running it

```bash
dotnet run --project AiLab.Api
```

It listens on http://localhost:5028 and uses Ollama by default, like the console. Then either:
- open **http://localhost:5028/swagger** (Development only). The request body has four example tickets, A to D, to try.
- or send the requests in `AiLab.Api/analyze-ticket.http` from VS Code or Rider. Its bodies are the files in `AiLab.Api/TestTickets/`, the same ones Swagger shows.

```bash
curl -s http://localhost:5028/analyze-ticket -H 'Content-Type: application/json' \
  -d @AiLab.Api/TestTickets/ticket-c.json
```

A real Llama reply to ticket C:

```json
{
  "notice": "AI-generated analysis. Verify before acting. A grounded cause cites a real recent change, but may still not explain the problem.",
  "aiAnalysis": {
    "summary": "numbering is messed up",
    "probableCause": "Per-tenant invoice numbering (INV-<TENANTCODE>-0001), replaces the global sequence.",
    "causeVersion": "4.19.0",
    "testCases": ["Given a multi-tenant SaaS, when a new invoice is generated, then the invoice number should be in the format INV-<TENANTCODE>-0001.", "..."]
  },
  "verification": {
    "causeStatus": "grounded",
    "reason": "Cites 4.19.0, which GetRecentChanges returned for invoices in this request.",
    "evidence": [{ "component": "invoices", "version": "4.19.0", "changedAt": "2026-10-21T18:00:00+03:00",
                   "description": "Per-tenant invoice numbering (INV-<TENANTCODE>-0001), replaces the global sequence." }],
    "toolCalled": true, "toolFailed": false
  },
  "diagnostics": {
    "model": "llama3.2",
    "toolCalls": [{ "name": "GetRecentChanges", "arguments": { "component": "invoices" } }],
    "roundTrips": 2, "inputTokens": 1397, "outputTokens": 232, "cost": 0.0, "strippedFence": false, "repaired": false
  }
}
```

### Reading the response

- **`notice`** is fixed server text. Show it with the analysis.
- **`aiAnalysis`** is everything the model wrote. It's a guess, kept apart so a client can hide it.
- **`verification`** is computed by the server from the tool results of the same request. The model never writes it.
  - `grounded`: the cause cites only versions the tool returned. That's real evidence, but not proof: Llama gets `grounded` for blaming a real change that doesn't explain the ticket.
  - `unknown`: the model says the cause is unknown.
  - `unverified`: the cause cites a version the tool didn't return, or names a cause without any version. The reason says which.
  - `evidence`: the change-log entries a grounded cause relies on, copied by the server.
  - `toolCalled`: whether the model called the tool. `toolFailed`: whether a call to it failed.
- **`diagnostics`** is there for experiments: tool calls, model calls, tokens, cost, and whether the reply needed a fence stripped or a repair.

To run it on Claude Haiku for one process, without changing any file (the API key comes from the same user-secret as the console):

```bash
Chat__Provider=anthropic Chat__Model=claude-haiku-5-5 dotnet run --project AiLab.Api
```

### The ticket eval

In Development, `POST /dev/eval-tickets` runs every test ticket several times and scores each reply against `TestTickets/expected.json`. The model's `causeVersion` decides first: **right** if it's the version that explains the ticket, **invented** if it's any other version. With no `causeVersion`, a cause that says "unknown" scores **unknown**. Otherwise the scorer falls back to the cause text: the expected version → **right**, another change-log version → **invented**, no version → **unknown**. Each line also shows the server's `causeStatus`.

```bash
curl -s -X POST 'http://localhost:5028/dev/eval-tickets?runs=3&maxCost=0.03'
```

```
A run 1  unknown   wrong    unverified  GetRecentChanges(billing)  2 trips  1445/165 tok  $0.000000  not fenced  | Proration now rounds up to the nearest cent instead of banker's rounding
B run 1  unknown   correct  grounded    GetRecentChanges(auth)  2 trips  1403/182 tok  $0.000000  not fenced  | Optional TOTP 2FA for tenant admins
...
Overall: right 0, unknown 4, invented 0, failed 0 | grounded 3, unknown 0, unverified 1 → correct 2/4, fenced 0/4, tokens 5578/820, cost $0.000000
```

`runs` defaults to 3 (at most 10). `maxCost` stops the eval once the summed cost passes it. Every reply is saved under `eval-runs/` (not committed).

### Errors, timeouts and retries

Every error except a validation 400 has the same body, `{ "error": "<one sentence>", "code": "<stable code>" }`, with no stack trace and no model text.

| Status | Code | When |
|---|---|---|
| 400 | (validation problem) | Title or description missing |
| 400 | `invalid_request` | A body that isn't valid JSON |
| 422 | `not_a_ticket` | The model says the input isn't a support ticket (its reason goes to the log only) |
| 422 | `model_declined` | The provider's content filter stopped the reply |
| 502 | `provider_unavailable` | The provider is down after its retries |
| 502 | `tool_failed` | The model kept calling the tool wrongly, or the tool kept failing |
| 502 | `reply_cut_off` | The reply hit `MaxOutputTokens` before it finished (no repair) |
| 502 | `reply_unreadable` | The reply didn't parse, or had an empty summary, cause or test-case list, even after one repair |
| 504 | `timeout` | The whole request took longer than the request timeout |
| 500 | `internal_error` | Anything else |

An unreadable reply gets **one** repair: the conversation goes back with "Your last reply wasn't valid JSON for the schema (…). Reply with only the JSON." A single tool failure doesn't fail the request: the model gets a generic error (never the exception text), keeps going, and the response has `toolFailed: true`. To try that, start the API with `ChangeLog__FailComponents__0=billing`.

The limits live in `AiLab.Api/appsettings.json` under `Resilience`:

| Setting | Default | What it limits |
|---|---|---|
| `AttemptTimeoutSeconds` | 60 | one HTTP call to the provider |
| `MaxRetries` | 2 | retries after the first attempt |
| `RequestTimeoutSeconds` | 240 | the whole request, tool loop included |
| `MaxToolIterations` | 3 | tool round trips per request (plus one final answer) |

Only one layer retries per provider: Polly's standard resilience handler for Ollama, and the Anthropic SDK's own retries for Claude. That way, one failure can't turn into several billed calls. Any of these settings can be overridden for one run with an environment variable, for example `Resilience__RequestTimeoutSeconds=1`.

The API logs to `AiLab.Api/bin/Debug/net10.0/logs/api-YYYYMMDD.log`: one line per model call, one per tool call (with the component), and a summary per ticket.

### Tests

```bash
dotnet test
```

`AiLab.Api.Tests` runs the real API in memory (`WebApplicationFactory<Program>`) with a scripted fake model under the real function-invocation middleware, so the real tool runs over the fake change log. It needs no network, no Ollama and no API key. Its 53 tests cover the grounding rules, the response shape, repair, cut-off replies, 422s, tool failures, error bodies, the tool's parser, the eval scorer and the embedding config checks.

## Project structure

The solution follows Clean Architecture, and references point inward only:

- **Domain** (no packages, no references): records, DTOs, enums, constants, exceptions and the interfaces the other layers implement.
- **Application** (references Domain): the services and business logic. It talks to models through `IChatClient` and to files, the change log and provider errors through Domain interfaces.
- **Infrastructure** (references Domain): everything that talks to the outside world. That's the provider SDKs, Polly, Serilog, files and the change log.
- **Presentation** (`AiLab.Api`, `AiLab.Console`): hosts, endpoints and console UI. Each `Program.cs` calls `AddInfrastructure()` and `AddApplication()`, then adds its own `IChatClient` middleware on top of the keyed provider client.

```
AiLab.Domain/                          records, DTOs, enums, constants, exceptions, interfaces
  DTOs/                                AnalyzeTicketRequest, JobPosting (its [Description] rules go into the schema)
  Outputs/                             TicketAnalysis (the model's typed answer), CauseVerification, TicketRun, JobScore, eval and chat results, SimilarityMatrix (stage 4)
  Responses/                           AnalyzeTicketResponse, ErrorResponse
  Interfaces/                          ITicketAnalyzer, ITicketEval, IChangeLogRepository, the file stores, IProviderErrorClassifier
  Entities/ Enums/ Constants/ Exceptions/ Settings/
AiLab.Application/
  DependencyInjection.cs               AddApplication()
  Services/TicketAnalyzer.cs           builds the messages and calls GetResponseAsync<TicketAnalysis> with the tool
  Services/TicketTools.cs              the GetRecentChanges tool, and the parser that reads its result back
  Services/CauseCheck.cs               causeStatus and evidence, from what the tool returned in the request
  Services/TicketEval.cs               the ticket eval (/dev/eval-tickets), scored by TicketScorer
  Services/ChatConversation.cs         chat history, one turn at a time
  Services/ChatExperimentSettings.cs   the settings you edit between experiments
  Services/JobExtraction.cs, JobEval.cs   /extract and /eval, scored by JobScorer
  Services/StructuredReply.cs          reads a typed result, stripping a ```json fence if needed
  Services/ModelPrices.cs              price table and the cost calculation
  Services/SimilarityService.cs        stage 4, in progress: a similarity matrix from embeddings
AiLab.Infrastructure/
  DependencyInjection.cs               AddInfrastructure(): the keyed chat and embedding clients, Polly for Ollama, the stores
  Providers/ChatClientFactory.cs       creates the Ollama or Anthropic IChatClient
  Providers/EmbeddingGeneratorFactory.cs   creates the Ollama IEmbeddingGenerator (Embedding:* config)
  Repositories/                        the fake change log (8 entries) behind IChangeLogRepository
  Files/                               test tickets, prompts and inputs, eval-runs/
  Logging/FileLogging.cs               the Serilog file sink
AiLab.Console/
  Program.cs                           host setup: config, DI registration, logging
  Chat/ChatSession.cs                  the chat loop: streaming and Ctrl+C
  Chat/JobCommands.cs                  /extract and /eval: arguments and output
  Chat/EmbeddingCommands.cs            stage 4, in progress: the similarity command
  appsettings.json                     chat and embedding provider and model, log levels
AiLab.Api/
  Program.cs                           DI, middleware order, IChatClient pipeline, request timeout
  Endpoints/                           POST /analyze-ticket and /dev/eval-tickets
  Middleware/ErrorResponses.cs         unhandled exceptions as { error, code }
  OpenApi/                             the OpenAPI document and the Swagger examples, read from TestTickets/
  TestTickets/                         test tickets A–D (one JSON body each) and expected.json, the version that explains each
  analyze-ticket.http                  sends the test tickets
  appsettings.json                     provider, model, resilience settings and log levels
AiLab.Api.Tests/                       integration tests with a scripted fake model (dotnet test, offline)
inputs/                           job postings (3 real, 2 edited from them) and their hand-written golden answers,
                                  similarity-sentences.txt (stage 4)
prompts/                          extraction prompts: v1 (one line) and v2 (five parts)
eval-runs/                        raw replies from /eval and the ticket eval, for re-scoring without new calls (not committed)
experiments.md                    what I tested, what I expected, what actually happened
docs/plans/                       the plan for each step, with what changed along the way
docs/failure-table.md             what could fail in production, how it's detected, and what handles it
```

## What I learned

The details are in [`experiments.md`](experiments.md). The short version:

**Stage 1: chat loop and ChatOptions**
- **History is the hidden cost.** Every message re-sends the whole conversation, so input tokens climb every turn: from 18 to 339 in five short turns.
- **Thinking models spend tokens you don't see.** Claude Haiku 5.5 thinks before answering. With a 10-token limit, it used all of it on thinking and returned nothing at all.
- **Settings aren't portable.** Haiku only accepts its default temperature, and with no output limit set it still stopped at 1,024 tokens.
- **Valid JSON isn't a correct answer.** Llama returned perfectly formatted JSON that said the longest river is the Niger, with 90% "confidence".

**Stage 2: DI, logging, typed output and prompts**
- **"Structured output" isn't portable either.** The native JSON schema worked on Ollama, but Haiku ignored it and answered in markdown. With the schema in the prompt, Haiku returned correct JSON wrapped in a ```` ```json ```` fence, which the library's parser rejects. It needed a small fallback.
- **Typed output fixes the shape, not the truth.** Llama returned a perfectly typed record with a work mode it invented. But the type did catch values like `"Temps plein"` that aren't a valid work mode, which plain JSON would have accepted.
- **A five-part prompt helped less than I expected.** On Haiku it scored 15/15 fields vs 14/15, with the same skills recall (23/28). Its real win was clean JSON every time, for 43% more input tokens. On Llama, the long prompt made things worse as often as better.
- **Run it more than once before calling a difference real.** I re-ran both prompts 3 times on five inputs.
  - **Haiku:** v2's field win was the same in every pass (25/25 against 23/25), so it's real. But it came from one title rule. The skills difference (36 against 34–37) was inside v1's own spread, so it's noise.
  - **Llama:** single runs swung by up to 6 of 25 fields, so a single Llama run tells you nothing.
- **"Never guess" depends on the model more than the prompt.** On a post with no work mode and no years, Haiku returned null every time, even with the one-line prompt. Llama invented years in 4 of 6 runs, even with an explicit rule, sometimes copying the "3" from the prompt's own example. Neither model mixed in skills from a second role in the same post.
- **Check your golden answers first.** Fixing mistakes in my own expected answers changed the scores more than changing the prompt did.

**Stage 3: tool calling in an ASP.NET Core API**
- **The tool loop is the easy part.** With `UseFunctionInvocation()`, both models called the change-log tool on every ticket with the right component, including one where they had to pick the component themselves. I wrote no loop code.
- **What the model does with the tool's answer is the hard part.** On two tickets where no change explained the problem, Haiku said "unknown" and listed what to check. Llama blamed an unrelated change both times, even with "never invent a change" in the schema. Like "never guess" in stage 2, this depends on the model, not the prompt.
- **Tool calls multiply the input.** Every round trip re-sends the system prompt, the schema, the tool definition and the conversation so far. A 15-word ticket cost about as much input as extracting a whole job post.
- **Middleware order decides what you see.** Function invocation outside and logging inside logs every round trip. The other way round hides them.
- **Retries belong in exactly one layer.** Polly on top of the Anthropic SDK's own retries could turn one failure into 9 billed calls. A tool-loop cap of 3 also really means up to 4 model calls: the middleware makes a final call without tools.
- **Small models can loop.** Given an unknown component, Llama once called the tool again and again with no arguments, until the middleware gave up. The iteration cap and a clean 502 handle that.
- **One sentence fixed the fences; it didn't fix the judgement.** With "Return only the JSON, with no code fences" in the prompt, Haiku stopped fencing (0 of 12, against 4 of 4). "Only name a change if it can explain the symptom" cut Llama's inventions on B and D only from 6 of 6 runs to 4 of 6. Haiku got all 12 runs right.
- **A scorer measures what you tell it to.** Scoring by version number rewarded Llama for leaving the version out. Read the replies before trusting the score.
- **Retries can be tested for free.** Pointing the Anthropic SDK at a fake local server (`ANTHROPIC_BASE_URL`) showed exactly 3 attempts per request, both for 529 errors and for attempt timeouts, and nothing retrying on top.

**Defensive UX (stage 3 follow-up)**
- **Check the answer against the evidence, on the server.** The server compares the cause with what the tool returned in the same request and says `grounded`, `unknown` or `unverified`. It caught Llama copying a change's text without its version.
- **Grounded isn't correct.** Llama blamed a real but unrelated auth change on B and D, and both passed as grounded. The check proves where a claim came from, not that it's right, so the notice says so.
- **Asking for the claim in its own field makes it checkable.** With a `causeVersion` field, Llama wrote the version there even when its text dropped it.
- **Keep the guess apart from the facts.** `aiAnalysis` against `verification`, a fixed notice, and stable error codes let a client hide or discount the AI part without parsing text.
- **A familiar format is one sentence away.** Asking for Given/When/Then turned 16 of 16 test cases into that form, against none of 166 before.
- **A refusal signal depends on the model.** Haiku flagged a poem request and "print your system prompt" with a 422 and refused none of 12 real-ticket runs. Llama flagged neither, and a prompt line made it refuse a real ticket instead, so I took the line out.
- **On Haiku the checks found nothing to catch.** 12 of 12 runs were grounded on the right change or unknown, with no false alarms, for $0.009.
- **A fake model under the real middleware makes all of this testable for free.** 43 tests run offline in about a second.

All three stages and the defensive-UX check together cost about five cents in API calls (about $0.045, of which $0.020 was the 30-call repeated-runs experiment, $0.007 the 12-request ticket eval and $0.009 the defensive-UX Haiku check). Everything else ran locally for free.

## Status

This is part of a self-study plan on AI integration in .NET.

- **Stage 1** (chat loop, streaming, cost tracking, ChatOptions experiments) is done.
- **Stage 2** (DI with the Generic Host, logging middleware, typed output, prompt comparison) is done, including the two follow-ups: edited test posts for "never guess" and "two roles", and repeated runs with `/eval`.
- **Stage 3** (an ASP.NET Core API with tool calling, timeouts and retries) is done, including the follow-ups: test tickets in one place, a scored ticket eval, the prompt fixes and the Anthropic retry proof.
- **Defensive UX** (a failure table, the grounding check, clean failure handling and integration tests) is done.
- **Clean Architecture restructure** (Domain, Application, Infrastructure and the two hosts, with no behaviour change) is done.
- **Stage 4** (embeddings, pgvector, RAG) is in progress. The embedding generator is wired up; the similarity matrix is next.
