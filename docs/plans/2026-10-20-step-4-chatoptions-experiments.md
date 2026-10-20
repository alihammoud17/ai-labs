# Step 4: ChatOptions experiments

- **Date:** 2026-10-20
- **Branch:** main
- **Goal:** Use the chat app as a lab bench: change one setting, run, and record what changes. Run every experiment on Llama first (free), then on Haiku for the comparison.
- **Status:** planned
- **Approval:** confirmed via /save-plan

## Steps

### 4.1 Wire in ChatOptions (once)
- [ ] At the top of `Program.cs`, create a `ChatOptions` object, outside the loop like `history`.
- [ ] Pass it as the second argument to `GetStreamingResponseAsync(history, options)`.
- [ ] Keep it simple: change the values in code between runs. Slash commands like `/temp 0.5` are a nice bonus, but not needed.

✅ The app still chats normally with an empty `ChatOptions`.

### 4.2 Experiment: temperature (section 2)
- [ ] Set `options.Temperature` to 0.
- [ ] Use `/reset` or restart between runs so history doesn't influence the answer. Each run should be a fresh conversation.
- [ ] Send the prompt from `experiments.md` 3 times and record the answers.
- [ ] Repeat with 0.5 and 1.0, then do all three on Haiku.
- [ ] Bonus: at temperature 0, ask a factual question the model gets wrong, such as an obscure fact or a fake library. It will often give the same wrong answer every time.

Watch for: Anthropic accepts 0–1. If any provider rejects a value, record the error as a finding.

### 4.3 Experiment: MaxOutputTokens and finish reason (section 3)
- [ ] Set `options.MaxOutputTokens = 10` and send the HTTPS prompt.
- [ ] After streaming, read `FinishReason` from the `ChatResponse` you already build with `ToChatResponse()`. Print it.
- [ ] Add a warning: if `FinishReason` is `ChatFinishReason.Length`, print something like `⚠ Answer was cut off`.
- [ ] Run again with no limit (`null`) and compare: you should see `Stop` instead of `Length`.
- [ ] Do both on Haiku too.

Think about: the 10-token answer looks like normal text that just ends. Without the finish reason, how would your app know it's incomplete?

### 4.4 Experiment: format-forcing system message (section 4)
- [ ] Write a system message, for example: `Reply ONLY with valid JSON in this shape: {"answer": string, "confidence": number between 0 and 1}. No other text.` Paste the exact text into `experiments.md`.
- [ ] Add it as the first message in history, with `ChatRole.System`. Make sure `/reset` keeps it.
- [ ] After each reply, try to parse the text with `JsonDocument.Parse` inside a try/catch. Print `✅ valid JSON` or `❌ invalid`.
- [ ] Send the 5 prompts from `experiments.md`, including the ones designed to break the format: the poem, "ignore the format", and an Arabic question.
- [ ] Count successes for Llama and Haiku.

Watch for: models often wrap JSON in ```` ```json ```` fences, which fails a strict parse. Record it, since it's a real production issue, and decide whether your parser should strip fences.

### 4.5 Write conclusions
- [ ] For each section, 1–2 lines: what you expected, what happened, what you'd do in a real app.
- [ ] Fill in "Overall takeaways" and "Total spent."

### Stage complete
- [ ] All four sections in `experiments.md` have both Llama and Haiku results.
- [ ] Commit, and send me or Claude Code your conclusions for review.

## Tests
All manual (E2E, run with `dotnet run --project AiLab.Console`; switch provider and model in `AiLab.Console/appsettings.json`):
- **4.1:** with an empty `ChatOptions`, the app still chats normally.
- **4.2:** each temperature (0 / 0.5 / 1.0) is run 3 times in a fresh conversation, on Llama and Haiku, and the answers are recorded in section 2. Any value a provider rejects is recorded as a finding.
- **4.3:** with `MaxOutputTokens = 10`, the finish reason prints `Length` and the `⚠ Answer was cut off` warning shows. With no limit (`null`), it prints `Stop`. Both on Llama and Haiku.
- **4.4:** after each reply, the strict `JsonDocument.Parse` check prints `✅ valid JSON` or `❌ invalid`. The success count for the 5 prompts is recorded for Llama and Haiku.
- **Stage complete:** all four sections in `experiments.md` have both Llama and Haiku results.

## Decisions & trade-offs
- Llama first because it's free; Haiku only for the comparison runs.
- `ChatOptions` values are changed in code between runs. Slash commands such as `/temp 0.5` are optional extras.
- Each temperature run starts a fresh conversation (`/reset` or a restart) so earlier history can't influence the answer.
- The finish reason comes from the `ChatResponse` already built with `ToChatResponse()`.
- JSON validity is checked with a strict `JsonDocument.Parse`. Whether to strip ```` ```json ```` fences is decided from the results.

## Alternatives considered
- Slash commands (`/temp 0.5`) instead of editing values in code: allowed as a bonus, not required.

## Out of scope
- Anything beyond the four `experiments.md` sections and the "Overall takeaways" / "Total spent" notes.
