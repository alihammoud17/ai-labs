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
- Secrets: `dotnet user-secrets`, key `Anthropic:ApiKey`. Never put keys in code or appsettings.

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

## Current stage: DI, logging, typed output, prompt evaluation

Branch `stage-2-di-logging-structured-output`. Plan: `docs/plans/2026-10-21-stage-2-di-logging-structured-output.md`.

1. [x] Same code on 2 providers: Generic Host + `AddChatClient`, chat loop moved into `ChatSession`
2. [x] `UseLogging` middleware, log levels in `appsettings.json`
3. [x] Typed output with `GetResponseAsync<T>`, `/extract` for job posts
4. [x] Prompt v1 vs five-part v2 on 3 real job posts, scored against golden answers → section 5 of `experiments.md`

**Current step:** Stage complete. Next: fix the golden-answer issues listed in `experiments.md` section 5, review the conclusions, and merge the branch.

## How to help me (tutor mode)

- I'm learning. Don't write the full solution unless I explicitly ask for it.
- Explain the concept and why it matters, then give hints: API names, docs to read, what to check.
- Review my code and point at problems rather than rewriting it.
- Develop and debug against Ollama to keep costs at zero; use Anthropic only for recorded experiment runs.
- Keep explanations short and plain.

## Conventions

- Commit after each completed step.
- Record every experiment result in `experiments.md` (expected vs actual, tokens, cost).