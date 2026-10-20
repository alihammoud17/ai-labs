# Step 3: Usage → cost per turn and session total

- **Date:** 2026-10-20
- **Branch:** main
- **Goal:** Add the Anthropic provider, then turn each streamed turn's token usage into a dollar cost, with a running session total.
- **Status:** in-progress
- **Approval:** confirmed via /save-plan

Finish and test each piece before starting the next.

## Steps

### Part A: add Anthropic (≈10 min)
- [x] **A1. Install the packages** (inside `AiLab.Console`): `dotnet add package Anthropic` and `dotnet add package Microsoft.Extensions.Configuration.UserSecrets`.
- [x] **A2. Store your key:** `dotnet user-secrets init`, then `dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."`.
- [x] **A3. Update `ChatClientFactory`:** add an optional `apiKey` parameter and an `"anthropic"` case that creates an `AnthropicClient` with that key and calls `.AsIChatClient(model)`.
- [x] **A4. Load the key in `Program.cs`:** build a `ConfigurationBuilder` with `.AddUserSecrets<Program>()` and read `config["Anthropic:ApiKey"]`.
- [x] **A5. Test:** switch to `Create("anthropic", "claude-haiku-5-5", key)` and chat for one turn. You should get an answer. Commit.

### Part B: cost per turn
- [ ] **B1. Put the model name in one variable.** At the top of `Program.cs`, store the provider and model in variables (`provider`, `model`) and pass them to the factory. `model` is needed later for the price lookup.
- [ ] **B2. Create the price file.** Add `Pricing/ModelPrices.cs` with:
  - a `record ModelPrice` with two `decimal` properties: input price per million and output price per million
  - a `static class ModelPrices` with a `Dictionary<string, ModelPrice>` holding `claude-haiku-5-5`, `claude-sonnet-5-5` and `llama3.2`, with prices from `CLAUDE.md`

  It should compile.
- [ ] **B3. Add a cost method** to the same static class. It takes a model ID, an input token count and an output token count, and returns the cost as `decimal`:
  - look up the model with `TryGetValue`
  - if not found, decide what to return (`0`, or `null` meaning "unknown")
  - otherwise: `input / 1_000_000m × InputPerMillion + output / 1_000_000m × OutputPerMillion` (the `m` suffix keeps the math in `decimal`)
- [ ] **B4. Get the usage from your stream.** After the streaming loop, turn the collected updates into a `ChatResponse` with `ToChatResponse()`, then read `response.Usage?.InputTokenCount` and `OutputTokenCount`.
- [ ] **B5. Calculate and print.** Call the cost method with `model` and the two counts, then print:
  `Tokens: in=152, out=48 | Turn: $0.000039 | Session: $0.000112`
  Keep `decimal sessionTotal = 0` **outside** the loop and add each turn's cost to it. Print costs with enough decimals (for example `"0.000000"`), otherwise they show as `$0.00`.
- [ ] **B6. Handle nulls.** If the token counts are null, print `Tokens: unknown` instead of crashing (use `??` or an `if`).

### Check the step is done
- [ ] **Hand check:** take one turn's printed tokens, work out the cost on a calculator, and confirm it matches.
- [ ] **Ollama check:** switch back to Ollama. Tokens should print and the cost should be `$0`.
- [ ] **Fill section 1 of `experiments.md`:** write your Expected line, then run 5 turns on Haiku and copy the numbers in.
- [ ] **Commit**, and change "Current step" in `CLAUDE.md` to 4.

## Tests
All manual. There are no automated tests in the plan.
- **A5 (manual, E2E):** run with `Create("anthropic", "claude-haiku-5-5", key)`, send one message, and get an answer.
- **B2 (build):** `dotnet build AiLab.Console` compiles.
- **B3 (manual, unit-level):** call the cost method with `"claude-haiku-5-5"`, 1,000,000 input and 1,000,000 output tokens. The result must be `0.60`.
- **B4 (manual, E2E):** both token counts print after each answer. If they're null, set a breakpoint and inspect the last updates' `Contents`.
- **Hand check (manual, E2E):** one Haiku turn's printed cost matches a calculator.
- **Ollama check (manual, E2E):** tokens print and the cost is `$0`.
- **History growth (manual, E2E):** run 5 Haiku turns and record them in `experiments.md` section 1. Input tokens go up every turn.

## Decisions & trade-offs
- Prices live in code (`Pricing/ModelPrices.cs`) as `decimal`, keyed by model ID, copied from the `CLAUDE.md` price table (checked 2026-10-19).
- Usage comes from `updates.ToChatResponse().Usage`, because a streamed turn has no single response object while the loop is running.
- Null token counts print `Tokens: unknown` and don't crash the app.
- Develop and debug against Ollama ($0). Use Anthropic only for the A5 check and the recorded runs.
- Return type for an unknown model: if B3 returns `null`, the method must return `decimal?`, not `decimal`.

## Alternatives considered
- Read `UsageContent` directly from the updates' `Contents` instead of `ToChatResponse()`. Kept as the fallback for debugging B4.

## Out of scope
- Step 4 (ChatOptions experiments).
- Token-based history trimming. The current 20-message cap stays as it is.

## Changes
- **2026-10-20:** replaced the first version of the steps with a more detailed breakdown (A1–A5, B1–B6, completion checks). A1–A4 are already done in the code. A5 stays unticked until a real Anthropic turn has been checked.
- **2026-10-20:** A5 ticked: you tested a turn on Anthropic. Part A is done.
