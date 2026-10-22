# ai-lab

A .NET 10 console app for learning AI integration hands-on. Part of a ~12-week self-study plan to become an AI-enabled .NET specialist.

## About me

- .NET backend developer, 5 years of experience.
- Strong in C#, ASP.NET Core and SaaS architecture; new to LLM integration.
- Already studied: tokens, context windows, inference, sampling (temperature, top-p, top-k), hallucinations, reasoning models.

## Stack

- .NET 10 console app: `AiLab.Console` in solution `AiLab`.
- `Microsoft.Extensions.AI`: all app code talks only to `IChatClient`.
- Providers are created in `Providers/ChatClientFactory.cs`:
  - `"ollama"` → `OllamaSharp` (`OllamaApiClient`), local and free, model `llama3.2`.
  - `"anthropic"` → official `Anthropic` NuGet (v10+), `.AsIChatClient(model)`, model `claude-haiku-5-5`.
- Generic Host (`Program.cs`): `AddChatClient(...)` calls the factory, then `.UseLogging()`. `ChatSession` and `JobExtractor` are resolved from DI. The host is built, not run, so Ctrl+C stays with the chat loop.
- Provider and model: `Chat:Provider` / `Chat:Model` in `appsettings.json`. Keep it on Ollama; for a one-off Anthropic run, override with the env vars `Chat__Provider` / `Chat__Model`.
- Logging: Serilog to a daily file in `bin/Debug/net10.0/logs/`. Levels under `Serilog:MinimumLevel`; `Verbose` shows full requests.
- Typed extraction: `Extraction/` (`JobPosting`, `JobExtractor`, `JobParser`, `JobScorer`), `/extract [v1|v2] <path>`. Inputs and golden answers in `inputs/`, prompts in `prompts/`. Run the app from the repo root.
- Repeated scored runs: `/eval <v1|v2> [runs]` (default 3) over every `inputs/job-N.txt` with a golden answer; raw replies in `eval-runs/` (git-ignored) for offline re-scoring.
- Secrets: `dotnet user-secrets`, key `Anthropic:ApiKey`. Never put keys in code or appsettings. They're only loaded in Development (`Properties/launchSettings.json` sets it).
- Haiku 5.5 quirks found so far: only the default temperature (1.0) is accepted; it thinks by default and the thinking counts toward `MaxOutputTokens`; it ignored the native JSON-schema response format, so `JobExtractor` uses `useJsonSchemaResponseFormat: false`, and it may fence JSON in ```` ```json ````.

## Prices (per 1M tokens, checked 2026-10-19)

| Model | Input | Output |
|---|---|---|
| claude-haiku-5-5 | $0.10 | $0.50 |
| claude-sonnet-5-5 | $2.00 | $10.00 |
| llama3.2 (Ollama) | $0 | $0 |

## Stage 1 (done): chat loop + ChatOptions experiments

1. [x] Chat loop with history (`List<ChatMessage>`, `/exit`)
2. [x] Streaming with `GetStreamingResponseAsync`
3. [x] Usage → cost per turn and session total (add the Anthropic adapter before this step)
4. [x] ChatOptions experiments, recorded in `experiments.md`:
   - Temperature 0 / 0.5 / 1.0, same prompt 3x each
   - Tiny `MaxOutputTokens` → check `FinishReason`
   - Format-forcing system message (JSON only), count parse failures

## Stage 2 (done): DI, logging, typed output, prompt evaluation

Merged into `main` (PR #1). Plan: `docs/plans/2026-10-21-stage-2-di-logging-structured-output.md`.

1. [x] Same code on 2 providers: Generic Host + `AddChatClient`, chat loop moved into `ChatSession`
2. [x] `UseLogging` middleware, log levels in `appsettings.json`
3. [x] Typed output with `GetResponseAsync<T>`, `/extract` for job posts
4. [x] Prompt v1 vs five-part v2 on 3 real job posts, scored against golden answers → section 5 of `experiments.md`

Loose ends (done, plan: `docs/plans/2026-10-22-stage-2-loose-ends-eval.md`):

- [x] Two edited test posts: `job-4` (no work mode, no years: "never guess") and `job-5` (two roles: "extract the first role")
- [x] Repeated runs with `/eval`, 3 per prompt on both models → section 6 of `experiments.md` (Haiku: fields gap real but one rule, skills gap noise)

## Stage 3 (in progress): tool calling in ASP.NET Core

Plan: `docs/plans/2026-10-22-stage-3-tool-calling.md`. Branch `stage-3-tool-calling`. For this stage Claude writes the code and explains each change (overrides tutor mode).

1. [ ] `POST /analyze-ticket` returning a summary, a probable cause and test cases
2. [ ] One tool backed by a fake repository, invoked automatically by the model
3. [ ] DI registration of `IChatClient`
4. [ ] Timeouts and retries configured

**Current step:** Stage 3, Part 1 (`AiLab.Core` + `AiLab.Api` with DI).

## How to help me (tutor mode)

- I'm learning. Don't write the full solution unless I explicitly ask for it.
- Explain the concept and why it matters, then give hints: API names, docs to read, what to check.
- Review my code and point at problems rather than rewriting it.
- Develop and debug against Ollama to keep costs at zero; use Anthropic only for recorded experiment runs.
- Keep explanations short and plain.

## Conventions

- Commit after each completed step.
- Record every experiment result in `experiments.md` (expected vs actual, tokens, cost).
