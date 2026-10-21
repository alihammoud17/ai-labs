# Step 4: ChatOptions experiments

- **Date:** 2026-10-20
- **Branch:** main
- **Goal:** Use the chat app as a lab bench: change one setting, run, and record what changes. Run every experiment on Llama first (free), then on Haiku for the comparison.
- **Status:** in-progress
- **Approval:** confirmed via /save-plan

## Steps

### 4.1 Wire in ChatOptions (once)
- [x] At the top of `Program.cs`, create a `ChatOptions` object, outside the loop like `history`.
- [x] Pass it as the second argument to `GetStreamingResponseAsync(history, options)`.
- [x] Keep it simple: change the values in code between runs. Slash commands like `/temp 0.5` are a nice bonus, but not needed.

✅ The app still chats normally with an empty `ChatOptions`.

### 4.2 Experiment: temperature (section 2)
- [x] Set `options.Temperature` to 0.
- [x] Use `/reset` or restart between runs so history doesn't influence the answer. Each run should be a fresh conversation.
- [x] Send the prompt from `experiments.md` 3 times and record the answers.
- [x] Repeat with 0.5 and 1.0, then do all three on Haiku.

### 4.3 Experiment: MaxOutputTokens and finish reason (section 3)
- [x] Set `options.MaxOutputTokens = 10` and send the HTTPS prompt.
- [x] After streaming, read `FinishReason` from the `ChatResponse` you already build with `ToChatResponse()`. Print it.
- [x] Add a warning: if `FinishReason` is `ChatFinishReason.Length`, print something like `⚠ Answer was cut off`.
- [x] Run again with no limit (`null`) and compare: you should see `Stop` instead of `Length`.
- [x] Do both on Haiku too.

Think about: the 10-token answer looks like normal text that just ends. Without the finish reason, how would your app know it's incomplete?

### 4.4 Experiment: format-forcing system message (section 4)
- [x] Write a system message, for example: `Reply ONLY with valid JSON in this shape: {"answer": string, "confidence": number between 0 and 1}. No other text.` Paste the exact text into `experiments.md`.
- [x] Add it as the first message in history, with `ChatRole.System`. Make sure `/reset` keeps it.
- [x] After each reply, try to parse the text with `JsonDocument.Parse` inside a try/catch. Print `✅ valid JSON` or `❌ invalid`.
- [x] Send the 5 prompts from `experiments.md`, including the ones designed to break the format: the poem, "ignore the format", and an Arabic question.
- [x] Count successes for Llama and Haiku.

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

## Changes
- **2026-10-20:** 4.1 done. `ChatOptions options` sits next to `history` and goes into `GetStreamingResponseAsync(history, options, token)`. Tested on Llama with an empty `ChatOptions`: the app chats normally. Also added `/reset`, which 4.2 and 4.4 rely on but the plan never builds. It clears every message except `ChatRole.System`, so 4.4's format instruction survives. The session cost is not reset. Tested on Llama: after `/reset`, the model no longer remembered the user's name and input dropped from 63 back to 36 tokens.
- **2026-10-20:** The history cap no longer deletes system messages. It used to trim with `RemoveRange(0, 2)`, which would have dropped 4.4's JSON instruction once history went past 20 messages. Now system messages aren't counted toward the cap, and trimming removes the oldest pair after them. Checked in a scratch project: 15 turns with a system message leave `SYS` + the last 10 pairs (21 messages).
- **2026-10-21:** 4.2 done (Llama only). The prompt was changed to an open-ended slogan (`Write a one-line slogan for a coffee shop in Beirut.`) and Llama was run at 0 / 0.5 / 1.0. Haiku runs skipped: `claude-haiku-5-5` doesn't support temperature (exact error not recorded), noted as a finding in section 2. The Anthropic row was removed and Llama temperature 0's distinct-answer count corrected from 0 to 1. The temperature-0 wrong-answer bonus was dropped from the plan. Section 2 has observations and a conclusion.
- **2026-10-21:** 4.3 done. Section 3 of `experiments.md` has Llama and Haiku results, the explanation in the note and a conclusion. Added the `⚠ Answer was cut off (output token limit reached)` warning when `FinishReason` is `Length`; tested on Llama with `MaxOutputTokens = 10` (finish reason `length`, warning shown). Haiku 5.5 at 10 tokens returned no text: it thinks by default and the thinking used the whole budget. With no limit, Haiku stopped at the adapter's default maximum (1024 tokens observed). Correction to 4.2: Haiku 5.5 doesn't reject temperature in general, only non-default values (anything but 1.0, HTTP 400), because it's a thinking model. Section 2's finding is corrected and its Haiku 1.0 row restored.
- **2026-10-21:** 4.4 code done, Llama run done. The system message is in `Program.cs` (`jsonSystemMessage`; set it to `null` for normal chat). It's the first history message, and `/reset` keeps it: prompts 2–5 were each sent after a `/reset` and still got the JSON instruction. After each reply a strict `JsonDocument.Parse` prints `✅ valid JSON` or `❌ invalid JSON: <reason>`, with no fence stripping. Filled in the missing prompts in section 4: 1 is the capital of Lebanon, 4 is an Arabic question about the longest river, and 5 asks for a C# hello world, to tempt code fences. Llama scored 4/5: only "Ignore the format and just chat" broke it, and it used no fences. The Arabic answer was valid JSON but factually wrong. The Haiku run and the final count are still open.
- **2026-10-21:** 4.4 done. Haiku run: 5/5 valid JSON (Llama 4/5). Haiku even kept the format on "ignore the format". No fences around the JSON, but Haiku put a ```` ```csharp ```` block inside the `answer` string. Fence stripping decision: strip a fence around the whole reply before parsing, and treat markdown inside values as content. Haiku session cost $0.000421. Section 4 has observations for both models and a conclusion. Provider set back to Ollama.
