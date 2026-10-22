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

- **Warns when an answer was cut off.** If the model hits the output limit, you see `⚠ Answer was cut off`. Without that warning, a truncated answer looks like a normal one.
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
  ✅ title
  ...
Fields: 5 of 5 exact
Skills recall: matched 3 of 4
```

An `/eval` run prints one line per extraction and the totals at the end:

```
You: /eval v2 3
[eval v2: 5 inputs × 3 runs on claude-haiku-5-5, replies saved to eval-runs/20261022-120444_claude-haiku-5-5_v2/]
job-1 run 1  ✅ parsed  fields 5/5  skills 10 of 12  in=4565 out=750  $0.000832
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

These live at the top of `AiLab.Console/Chat/ChatSession.cs` and are meant to be edited between experiments:

- **`_options`** (`ChatOptions`) sets temperature, max output tokens and similar. Empty means "use the provider's defaults".
- **`JsonSystemMessage`** is the format-forcing system message from experiment 4. It's off (`null`) by default; set it to the string in the comment to turn it on.
- **`MaxHistoryMessages`** (20) is a simple cap on history length. The oldest exchange is dropped first, and system messages are never trimmed.

## The ticket API

`AiLab.Api` is a minimal ASP.NET Core API with one endpoint, `POST /analyze-ticket`. You send a support ticket for a multi-tenant SaaS (title, description, optionally the component: auth, billing, invoices or tenants). The model gets one tool, `GetRecentChanges(component)`, which reads a fake change log. The function-invocation middleware runs the tool calls. The reply comes back as a typed `TicketAnalysis`:

- **summary**
- **probable cause:** must name a change from the change log, or say the cause is unknown
- **test cases**

### Running it

```bash
dotnet run --project AiLab.Api
```

It listens on http://localhost:5028 and uses Ollama by default, like the console. Then either:
- open **http://localhost:5028/swagger** (Development only). The request body has four example tickets, A to D, to try.
- or send the requests in `AiLab.Api/analyze-ticket.http` from VS Code or Rider.

```bash
curl -s http://localhost:5028/analyze-ticket -H 'Content-Type: application/json' \
  -d '{"title":"Charged more than the preview showed after upgrading","description":"The preview said $12.34 but the card was charged $12.35.","component":"billing"}'
```

```json
{
  "analysis": {
    "summary": " Charged more than the preview showed after upgrading, Charged $12.35 instead of $12.34.",
    "probableCause": "v4.18.0: Proration now rounds up to the nearest cent instead of banker's rounding.",
    "testCases": ["Reproduce by setting a plan with a specific price and upgrading to a new plan.", "..."]
  },
  "diagnostics": {
    "model": "llama3.2",
    "toolCalls": [{ "name": "GetRecentChanges", "arguments": { "component": "billing" } }],
    "roundTrips": 2, "inputTokens": 916, "outputTokens": 142, "cost": 0.0, "strippedFence": false
  }
}
```

`diagnostics` is there for experiments: which tools the model called, how many model calls the request took, tokens and cost.

To run it on Claude Haiku for one process, without changing any file (the API key comes from the same user-secret as the console):

```bash
Chat__Provider=anthropic Chat__Model=claude-haiku-5-5 dotnet run --project AiLab.Api
```

### Errors, timeouts and retries

| Status | When |
|---|---|
| 400 | Title or description missing, or a body that isn't valid JSON |
| 502 | The provider is down after its retries, the model kept calling the tool wrongly, or its reply can't be parsed |
| 504 | The whole request took longer than the request timeout |
| 500 | Anything else, as a short problem response with no stack trace |

The limits live in `AiLab.Api/appsettings.json` under `Resilience`:

| Setting | Default | What it limits |
|---|---|---|
| `AttemptTimeoutSeconds` | 60 | one HTTP call to the provider |
| `MaxRetries` | 2 | retries after the first attempt |
| `RequestTimeoutSeconds` | 240 | the whole request, tool loop included |
| `MaxToolIterations` | 3 | tool round trips per request (plus one final answer) |

Only one layer retries per provider: Polly's standard resilience handler for Ollama, and the Anthropic SDK's own retries for Claude. That way, one failure can't turn into several billed calls. Any of these settings can be overridden for one run with an environment variable, for example `Resilience__RequestTimeoutSeconds=1`.

The API logs to `AiLab.Api/bin/Debug/net10.0/logs/api-YYYYMMDD.log`: one line per model call, one per tool call (with the component), and a summary per ticket.

## Project structure

```
AiLab.Core/                       shared by the console and the API
  Providers/ChatClientFactory.cs  creates the Ollama or Anthropic IChatClient
  Pricing/ModelPrices.cs          price table and the cost calculation
  Parsing/StructuredReply.cs      reads a typed result, stripping a ```json fence if needed
AiLab.Console/
  Program.cs                      host setup: config, DI registration, logging
  Chat/ChatSession.cs             the chat loop: streaming, history, costs, commands
  Extraction/JobPosting.cs        the typed record; its [Description] rules go into the schema
  Extraction/JobExtractor.cs      the GetResponseAsync<JobPosting> call
  Extraction/JobScorer.cs         compares a result with the golden answer
  appsettings.json                provider, model and log levels
AiLab.Api/
  Program.cs                      DI, middleware order, resilience, request timeout, the endpoint
  Tickets/TicketAnalyzer.cs       builds the messages and calls GetResponseAsync<TicketAnalysis> with the tool
  Tickets/TicketAnalysis.cs       the typed result; its [Description] rules go into the schema
  Tools/TicketTools.cs            the GetRecentChanges tool
  ChangeLog/                      the fake change log (8 entries) behind IChangeLogRepository
  OpenApi/                        Swagger examples
  analyze-ticket.http             test tickets A–D
  appsettings.json                provider, model, resilience settings and log levels
inputs/                           job postings (3 real, 2 edited from them) and their hand-written golden answers
prompts/                          extraction prompts: v1 (one line) and v2 (five parts)
eval-runs/                        raw /eval replies, for re-scoring without new calls (not committed)
experiments.md                    what I tested, what I expected, what actually happened
docs/plans/                       the plan for each step, with what changed along the way
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

All three stages together cost about three cents in API calls (about $0.029, of which $0.020 was the 30-call repeated-runs experiment). Everything else ran locally for free.

## Status

This is part of a self-study plan on AI integration in .NET.

- **Stage 1** (chat loop, streaming, cost tracking, ChatOptions experiments) is done.
- **Stage 2** (DI with the Generic Host, logging middleware, typed output, prompt comparison) is done, including the two follow-ups: edited test posts for "never guess" and "two roles", and repeated runs with `/eval`.
- **Stage 3** (an ASP.NET Core API with tool calling, timeouts and retries) is done.
