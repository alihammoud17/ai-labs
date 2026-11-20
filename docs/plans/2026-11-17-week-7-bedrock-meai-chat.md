# Bedrock via MEAI Day Plan: Call Bedrock Through the Same Abstraction

- **Date:** Tue 17 Nov 2026 (Week 7, theme Bedrock via MEAI), worked from Fri 2026-11-20, on aws-emulator
- **Branch:** `stage-7-bedrock-meai` (from `main`), not created yet
- **Status:** not started. Reviewed 2026-11-20 against the repo, the package source (1.1.3) and the emulator; refinements R1–R11 applied.
- **Focus:** Call Bedrock through the same abstraction (`IChatClient`) as Ollama and Anthropic
- **Where:** `AiLab.Infrastructure` (the provider factory, DI, error classifier), `AiLab.Application` (prices), the console host for the run. No new project.

Tutor mode: I type the commands and write the code; Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

After each finished step, Claude adds a short **How we did it** note under it when I ask.

## Where things stand

| Block | Status |
|---|---|
| 1. Concepts | not done |
| 2. Package and factory | **done** |
| 3. Emulator plumbing (endpoint, TLS, credentials) | **done** |
| 4. Streaming + usage in the console → D1 | **done**, D1 ✅ |
| 5. Errors, cancellation, prices | **done** (ticket analyzer + tools on Bedrock: open) |
| 6. Tests | **done** |
| 7. Housekeeping | **done** (PR #15) |

**Minimum:** 2 → 3 → 4 → 7. Then 5 and 6.

## Deliverable
- [x] **D1. ai-lab calls Bedrock through `IChatClient` (Converse), with streaming and usage:** `Chat:Provider = bedrock` makes the console chat stream a reply through `ConverseStream` and print the usage line (input/output tokens, finish reason, cost), with no change outside Infrastructure except the price table and one cancellation filter in `ChatSession` (R2).
  - **Checked 2026-11-20:** the console streams 13 updates per turn through `ConverseStream` and prints `Tokens: in=2, out=38 | Finish Reason: stop | Turn: $0.000009 | Session: $0.000009`. Changes outside Infrastructure: the price table and the `ChatSession` cancel filter.

## Resources
- AWS.Bedrock.MEAI on NuGet (README): https://www.nuget.org/packages/AWS.Bedrock.MEAI/
- Its source (aws-dotnet-ai): https://github.com/aws/aws-dotnet-ai/tree/main/src/AWS.Bedrock.MEAI
- AWS .NET blog, Integrating Amazon Bedrock in .NET (package list only; its code is outdated): https://aws.amazon.com/blogs/dotnet/getting-started-with-amazon-bedrock-in-net-applications/
- Bedrock API reference, ConverseStream: https://docs.aws.amazon.com/bedrock/latest/APIReference/API_runtime_ConverseStream.html
- Bedrock pricing: https://aws.amazon.com/bedrock/pricing/
- Monday's setup: `docker/aws-setup/README.md` → "Into the MEAI day"

## Scope rules for today
- **No AWS account, no Anthropic calls.** Bedrock runs on aws-emulator (`https://127.0.0.1:9297`, account `000000000000`). The safety check from `docker/aws-setup/README.md` runs first in every terminal.
- **The emulator runs no model.** `Converse` and `ConverseStream` return a fixed "No language model runs in this emulator…" text, plus usage numbers. Today checks the wiring: that the stream arrives, that usage and finish reason reach `SessionCostTracker`, that errors and Ctrl+C behave. Not answer quality.
- **The package is `AWS.Bedrock.MEAI`**, not `AWSSDK.Extensions.Bedrock.MEAI`. The old one is deprecated (renamed and moved to the aws-dotnet-ai repo); the `AsIChatClient` extension stays in the `Amazon.BedrockRuntime` namespace.
- **Ollama stays the default** in `appsettings.json`. Bedrock is switched on per run with environment variables (`Chat__Provider`, `Chat__Model`), so nothing else changes behaviour.
- **One retry layer per provider**, as for Anthropic: the AWS SDK retries on its own, so Bedrock gets no Polly.

### What the review showed (2026-11-20)

Checked with the CLI and a throwaway .NET 10 file-based app in the scratchpad (AWS.Bedrock.MEAI 1.1.3, AWSSDK.BedrockRuntime 4.1.0.2), after the safety check:

- **Package:** `AWS.Bedrock.MEAI` 1.1.3 (2026-10-30, the same as the repo's `main`) needs `Microsoft.Extensions.AI.Abstractions` ≥ 10.8.3 (the solution has 10.11.0: fine) and `AWSSDK.BedrockRuntime` ≥ 4.0.17.5. Unpinned, NuGet takes the lowest, 4.0.17.5 on the 4.0 Core line; `AiLab.Bedrock` is on 4.1 (Core ≥ 4.1.1.1). No project references both, so no clash, but pin 4.1.0.2 (R1). `AsIChatClient(runtime, modelId)` is not `[Experimental]`; `ProviderName` is `"aws.bedrock"`.
- **Model ID:** `converse` and `ConverseStream` accept the profile `us.amazon.nova-lite-v1:0` and the bare `amazon.nova-lite-v1:0`. `us.anthropic.claude-haiku-5-5` and invented IDs → `ValidationException` (400, base `AmazonServiceException`).
- **Stream:** 13 updates: 1 empty (message start), 10 text pieces, 1 with finish `stop`, 1 with only `UsageContent` (the metadata event, last). `ToChatResponse()` → in 2, out 38, `stop`. Second turn: in 47 (the history), out 38 (the canned text is always 38).
- **Endpoint:** `AWS_ENDPOINT_URL` (set by `ai-lab-kb.env`) alone already sends the SDK to the emulator; credentials come from `AWS_SHARED_CREDENTIALS_FILE`.
- **Offline:** `new AmazonBedrockRuntimeClient(...)` + `AsIChatClient` work with no env vars and no credentials (resolved on the first call), so Block 6's factory test runs offline.
- **Emulator down:** bare `HttpRequestException` (already classified).
- **Cancel mid-stream:** all 13 updates still arrive (one burst), then `BedrockRuntimeEventStreamException("Error.")` with an inner `TaskCanceledException`. Not an `OperationCanceledException`, and its base is `EventStreamException`, not `AmazonServiceException` (R2, R3).
- **`GetResponseAsync<T>`:** the package forces a synthetic tool (`BedrockStructuredOutputMode.SyntheticTool`, the default); the emulator answers it with `{}` (R10).
- **Not checked:** Ctrl+C by hand (the reply takes ~1–60 ms), the SDK's `Timeout` and retries through a custom `HttpClientFactory` (R6), a run without credentials, AccessDenied (no IAM checks on the emulator).

---

## Block 1: Concepts (15 min) — not done

- [x] **`IChatClient` vs `AsIChatClient`.**
  - **What to do:** note the answer to the question in the roadmap: it's both. `IChatClient` is MEAI's interface (the "same abstraction"); `AsIChatClient` is the extension method in `AWS.Bedrock.MEAI` that wraps an `IAmazonBedrockRuntime` (the SDK client) and returns an `IChatClient`. Anthropic's SDK has the same pattern: `new AnthropicClient{…}.AsIChatClient(model)` in `ChatClientFactory`.
  - **Why:** the rest of ai-lab only ever sees `IChatClient`. Adding Bedrock is one new `case` in the factory, not a change to the chat loop.
- [x] **`GetResponseAsync` → `Converse`, `GetStreamingResponseAsync` → `ConverseStream`.**
  - **What to do:** note the IAM link from Monday: `Converse` needs `bedrock:InvokeModel`, `ConverseStream` needs `bedrock:InvokeModelWithResponseStream`. The console streams, so it uses the second.
  - **Why:** on the real account, a missing streaming permission breaks the console but not a non-streaming test.
- [x] **Where usage comes from in a stream.**
  - **What to do:** read how `BedrockChatClient` maps the stream's `metadata` event to a `UsageContent` in the last update (update 13 of 13 on the emulator), and how `ChatConversation.CompleteTurn` (`AiLab.Application/Services/ChatConversation.cs`) rebuilds one response with `updates.ToChatResponse()`.
  - **Why:** usage arrives at the end of the stream, not with the text. If the last update is dropped, the usage line says "Tokens: unknown".
- [x] **What `ChatClientMetadata` gives.**
  - **What to do:** note that the Bedrock client reports its provider name (`aws.bedrock`) and `DefaultModelId` (the model ID you passed). `SessionCostTracker` prices by that ID.
  - **Why:** `ModelPrices` is keyed by model ID, and Bedrock IDs (`us.amazon.nova-lite-v1:0`) aren't in the table yet. Block 5 adds them.

---

## Block 2: Package and factory (60 min) — done

### 2.1 Branch and package
- [x] `git switch main && git pull && git switch -c stage-7-bedrock-meai`
- [x] `dotnet add AiLab.Infrastructure package AWS.Bedrock.MEAI --version 1.1.3`, then `dotnet add AiLab.Infrastructure package AWSSDK.BedrockRuntime --version 4.1.0.2` (R1).
  - **Why pin the SDK:** the package asks for `AWSSDK.BedrockRuntime` ≥ 4.0.17.5, and NuGet takes the lowest version allowed, on the older 4.0 Core line. 4.1.0.2 keeps one AWS Core line with `AiLab.Bedrock` and with Week 8 Block 4's agent runtime in Infrastructure.
  - **Why Infrastructure:** it's the only project that knows provider SDKs (the Anthropic and Ollama packages live there). Domain and Application keep seeing only `IChatClient`.
- [x] `dotnet build`: no warnings about package downgrades (NU1605) or version conflicts. Expected from the review: `Microsoft.Extensions.AI.Abstractions` stays 10.11.0, `AWSSDK.Core` resolves to 4.1.x. `dotnet list AiLab.Infrastructure package --include-transitive | grep -E 'AWSSDK|Abstractions'` shows it.

### 2.2 A `bedrock` case in `ChatClientFactory`
- [x] Add `"bedrock"` to the `switch`: build an `AmazonBedrockRuntimeClient` from an `AmazonBedrockRuntimeConfig`, then `.AsIChatClient(model)`.
- [x] Config it needs, as new optional parameters (like `ollamaUrl` and the Anthropic ones):
  - **Region** (`Bedrock:Region`, default `us-east-1`) → `RegionEndpoint` (real account) or `AuthenticationRegion` (emulator).
  - **Service URL** (`Bedrock:ServiceUrl`, emulator only) → `ServiceURL`. Empty on the real account.
  - **Either / or (R4):** set `ServiceURL` + `AuthenticationRegion`, **or** `RegionEndpoint`, never both. In the AWS SDK, setting one clears the other, so the order of two lines would decide where calls go.
  - **Keep `Bedrock:ServiceUrl` explicit (R7)** even though `AWS_ENDPOINT_URL` from `ai-lab-kb.env` already redirects the SDK: the target is then visible in the config and the run command, not hidden in a shell variable that could leak into a later real-account run.
  - **Attempt timeout and retry count** from `ResilienceSettings` when the API passes them → the config's `Timeout` and `MaxErrorRetry`. Null keeps the SDK defaults.
  - **Why:** same shape as the Anthropic branch, so the API host gets one retry layer and the console keeps the defaults.
- [x] No credentials in config. The SDK's credential chain finds them: on the emulator from the variables in `~/.aws/emulator/ai-lab-kb.env`; on the real account from the `ai-lab-dev` profile (`AWS_PROFILE`).
  - **Why:** keys never sit in `appsettings.json` or user-secrets, and the same code works in both places.
  - **Done (handed over):** `"bedrock"` case → private `CreateBedrock(model, region, serviceUrl, attemptTimeout, maxRetries)` in `ChatClientFactory`. Four new optional parameters (`bedrockRegion`, `bedrockServiceUrl`, `bedrockAttemptTimeout`, `bedrockMaxRetries`); the Anthropic pair kept its names, since renaming it to a shared `sdk…` pair would also touch `AiLab.Evals`. Region defaults to `us-east-1`; with a service URL it sets `ServiceURL` + `AuthenticationRegion`, otherwise `RegionEndpoint` (never both). `Timeout` and `MaxErrorRetry` set only when given, so null keeps the SDK defaults. No credentials anywhere. The TLS factory (`Bedrock:EmulatorCaPath`) waits for Block 3. Build clean.

### 2.3 DI
- [x] In `DependencyInjection.AddInfrastructure`, pass the `Bedrock:*` values to the factory, and the resilience timeout/retries the same way as for Anthropic: always passed (`resilience?.AttemptTimeout`, `resilience?.MaxRetries`), ignored by the other branches.
- [x] Update the comments that name only two providers: the one at the top of `AddInfrastructure`, the one above the keyed `IChatClient` (three providers, which layer retries for each), the factory's header comment, and `ResilienceSettings.AttemptTimeoutSeconds` ("the SDK's Timeout for Anthropic and Bedrock").
  - **Done (handed over):** the keyed `IChatClient` lambda passes `Bedrock:Region`, `Bedrock:ServiceUrl` and `resilience?.AttemptTimeout` / `MaxRetries` as the `bedrock…` arguments (always passed, ignored by the other branches, as for Anthropic). Comments: top of `AddInfrastructure` (console and Rag: SDK defaults; Api: one retry layer each), above the keyed client (three providers, the SDK retries for Anthropic and Bedrock, no Polly, no Bedrock key in config), the factory header (2.2), `ResilienceSettings.AttemptTimeoutSeconds`. Build clean; `dotnet test`: 343 passed, 42 skipped (the usual env-gated evals and Postgres tests), 0 failed.

---

## Block 3: Emulator plumbing (45 min) — done

- [x] **Start and check:** `docker ps` shows `aws-emulator` (if not, `docker/bedrock-kb/bedrock-kb-emulator.sh`), then the safety check.
  - **Done:** 2026-11-20, container up (started container-only, no KB), health OK, safety check `SAFE`. Commands now in README "aws-emulator: start, check, stop".
- [x] **TLS (R5):** the emulator's certificate is self-signed. Copy the approach from `AiLab.Bedrock`'s `EmulatorHttpClientFactory` (bottom of `AiLab.Bedrock/Program.cs`: trust only that CA, load the certificate blocks only) into `AiLab.Infrastructure/Providers/EmulatorHttpClientFactory.cs`, `internal sealed`, taking the **CA path** (not the port, as the original does). Used only when `Bedrock:EmulatorCaPath` is set.
  - **Why:** macOS .NET ignores `SSL_CERT_FILE`, and turning off certificate checks would be the wrong habit. Scoped to one CA and one setting, it can't leak into the real-account path.
  - **Decision (written down):** copy, with a "duplicate of …" comment in both files. `AiLab.Bedrock` references nothing; referencing Infrastructure for 30 lines would pull in Anthropic, Npgsql, MCP and the rest. Week 8 Block 4 (`BedrockKbAskService` in Infrastructure) needs this class too; then `AiLab.Bedrock` can reference it or be retired.
  - **Done (handed over):** `AiLab.Infrastructure/Providers/EmulatorHttpClientFactory.cs` (`internal sealed`, CA path in the constructor; a missing file or a file with no certificate fails at client creation, naming `Bedrock:EmulatorCaPath`). New factory parameter `bedrockEmulatorCaPath`, passed from `Bedrock:EmulatorCaPath` in DI; set on `config.HttpClientFactory` only when given, and **refused without `Bedrock:ServiceUrl`**, so the emulator CA can't be trusted for a real AWS endpoint. A duplicate comment sits in both files. Checked through the real factory against the emulator: 13 updates, in 2 / out 38 / `stop`, provider `aws.bedrock`; both guard errors read as intended.
  - **Found:** AWSSDK.Core 4.1 honours `AWS_CA_BUNDLE` (set by `ai-lab-kb.env`): the factory without a CA path also reached the emulator, and failed with an SSL error once `AWS_CA_BUNDLE` was unset (`SSL_CERT_FILE` alone does nothing, as noted before). So the class is a belt next to the env file's braces: it works without the env file's CA variable and scopes trust to one setting, but the env var alone would do. **Decision (2026-11-20): keep the class**: trust is explicit in config and refused without the emulator URL, the Timeout copy is proven, and Week 8 Block 4 needs it anyway.
- [x] **Timeout through the factory (R6):** check whether the SDK still applies `clientConfig.Timeout` when the `HttpClient` comes from your factory (read `IClientConfig` / the SDK source, or set a 1 ms timeout once and see). If not, set `HttpClient.Timeout` from `clientConfig.Timeout` in the factory.
  - **Why:** otherwise the API's attempt timeout silently doesn't apply on the emulator path.
  - **Done (handed over):** measured, not read: with a custom factory the SDK **ignores** `config.Timeout` (a 1 ms timeout let the call succeed). `EmulatorHttpClientFactory` now copies `clientConfig.Timeout` onto the `HttpClient`; through the real factory, `bedrockAttemptTimeout` 1 ms → `System.TimeoutException` ("A task was canceled."). That exception is neither an `OperationCanceledException` nor an AWS type: Block 5's classifier must add it.
- [x] **Model ID on the emulator:** use `us.amazon.nova-lite-v1:0` (the profile). The review found both it and `amazon.nova-lite-v1:0` accepted by `converse` and `ConverseStream`; confirm from the console in Block 4 (Monday's open question, answered).
  - **Done:** the console ran on `us.amazon.nova-lite-v1:0` (profile) and printed `Chat started with aws.bedrock/us.amazon.nova-lite-v1:0`. Monday's open question: the profile works on `converse` and `ConverseStream`.

---

## Block 4: Streaming + usage in the console (45 min) → D1 — done

- [x] Run the console with Bedrock, from the repo root, after the safety check:
  ```bash
  source ~/.aws/emulator/ai-lab-kb.env
  Chat__Provider=bedrock Chat__Model=us.amazon.nova-lite-v1:0 \
  Bedrock__ServiceUrl=https://127.0.0.1:9297 Bedrock__EmulatorCaPath=$HOME/.aws/emulator/9297/ca.pem \
  dotnet run --project AiLab.Console
  ```
  - **Why env vars:** `Host.CreateApplicationBuilder` reads them on top of `appsettings.json`, so Ollama stays the default.
- [x] Send one message. Check:
  1. The reply **streams** (several updates, not one block). Expected from the review: 13 updates, 10 of them text.
  2. The **usage line** shows input/output tokens and a finish reason. Expected: in 2 (for a two-word message), out 38, `stop`.
  3. The **cost**: "unknown price for 'us.amazon.nova-lite-v1:0'" until Block 5. That's the expected result now, not a bug.
- [x] Send a second message: the whole history goes again (as with Ollama), and input tokens grow (review: 2 → 47).
- [x] Record the two turns (tokens, finish reason, number of updates) for `experiments.md`. ✅ **D1** once Block 5's price is in.
  - **Done (handed over, 2026-11-20 22:48):** console run from the repo root after the safety check, two messages piped in, `Serilog__MinimumLevel__Override__Microsoft.Extensions.AI=Verbose` (through `env`: a shell variable name can't hold a dot) to count updates in `ai-20261120.log`.

    | Turn | Updates | Tokens in / out | Finish | Cost line |
    |---|---|---|---|---|
    | 1 `Say hi` | 13 | 2 / 38 | `stop` | `unknown price for 'us.amazon.nova-lite-v1:0'` |
    | 2 `And again, a bit longer please` | 13 | 47 / 38 | `stop` | same |

    The emulator's text says it got 1 message, then 3 (user, assistant, user): the whole history goes again, as with Ollama. First update ~160 ms after the call. The reply printed as streamed text, the usage line came from `ToChatResponse()` over the updates (usage only in the 13th). No code change outside Infrastructure so far. D1 waits for Block 5's price.

---

## Block 5: Errors, cancellation, prices (45 min) — done

- [x] **Prices:** add Bedrock IDs to `ModelPrices`: `us.amazon.nova-lite-v1:0` and `amazon.nova-lite-v1:0`, plus `us.anthropic.claude-haiku-5-5` for the real account. Take the numbers from the Bedrock pricing page and write the date in the comment (Nova Lite is listed around $0.06 / $0.24 per 1M tokens by third parties: confirm on AWS's page).
  - **Why:** the usage line then shows a turn cost. On the emulator the token counts are the emulator's, so the cost is a wiring check, not a real cost.
  - **Done (handed over):** prices from AWS's public price list files (`pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonBedrock` and `…/AmazonBedrockFoundationModels`, us-east-1, checked 2026-11-20; the Price List API needs credentials). Nova Lite $0.06 / $0.24 (bare ID and `us.` profile); Haiku 5.5 `us.` profile $0.11 / $0.55 (the "regional" price, 10% over global), plus `global.anthropic.claude-haiku-5-5` $0.10 / $0.50. Third-party Nova numbers confirmed. Console turn 1: in 2 / out 38 → `$0.000009` (2 × 0.06 + 38 × 0.24 per 1M = $0.0000092), turn 2 in 47 → `$0.000012`, session `$0.000021`.
- [x] **Errors (R3):** `ProviderErrorClassifier` knows Ollama, Anthropic and Polly exceptions, not AWS ones. Add `AmazonServiceException` (every error the service returns) **and** `EventStreamException` (an error inside a stream doesn't derive from `AmazonServiceException`). Add `System.TimeoutException` too (the SDK's attempt timeout, found in Block 3; the services only treat `OperationCanceledException` without a caller cancel as a timeout today). Decide on `AmazonClientException` (a local problem, e.g. no credentials found): arguably a 500, not a provider outage; write the choice down.
  - **Check (R9):** run once with `Chat__Model=us.anthropic.claude-haiku-5-5`, the real target, which the emulator doesn't have: `ValidationException` 400. Expect the console's `[error: …; turn discarded]` and, in the API, the same error mapping as for the other providers.
  - **Why:** `/ask` and the ticket analyzer turn provider failures into clean errors only for exceptions the classifier recognises.
  - **Done (handed over):** classifier adds `AmazonServiceException`, `BedrockRuntimeEventStreamException` (public, in `Amazon.BedrockRuntime`; its base `EventStreamException` sits in an `.Internal` namespace) and `TimeoutException`. **Choice: `AmazonClientException` is not a provider failure** (no credentials, bad config: ours to fix, stays a 500). Checks: console with `us.anthropic.claude-haiku-5-5` → `[error: The provided model identifier is invalid.; turn discarded]`; `AiLab.Rag` `/ask` with it → `502 provider_unavailable` (the same as the other providers).
  - **Found:** `AiLab.Api` `/analyze-ticket` with it → `502 tool_failed`, without reaching the emulator. The package throws `ArgumentException: ResponseFormat cannot be used with Tools in Amazon Bedrock` (the default `SyntheticTool` mode spends Bedrock's one forced tool on the response format), and `TicketAnalyzer` maps `ArgumentException` to `tool_failed`. So **the ticket analyzer can't run on Bedrock as wired today**, with any model. Fix options: `BedrockStructuredOutputMode.Native` (composes with tools and streaming; Claude 4.5+, not older models), or no `ResponseFormat` (the schema is in the prompt already and `StructuredReply` parses the text). Open, for section 22 and a decision.
- [x] **Ctrl+C (R2, R8):** Bedrock reports a cancel as `BedrockRuntimeEventStreamException` (inner `TaskCanceledException`), so today the console would print `[error: Error.; turn discarded]`. In `ChatSession`, add `catch (Exception) when (cancellationToken.IsCancellationRequested)` to the cancelled path (rule: if I cancelled, it's a cancel, whatever the SDK throws). The existing `ThrowIfCancellationRequested` still covers SDKs that end the stream quietly.
  - **Alternative not taken:** a small delegating client in Infrastructure that turns that exception into `OperationCanceledException`. It keeps D1 "Infrastructure only" but fixes one SDK, while the filter fixes all of them.
  - **Check:** by hand it can't be done on the emulator (the reply arrives in one burst in ~1–60 ms). Rely on the review probe; on the real account, press Ctrl+C during a long Haiku answer.
  - **Done (handed over):** `ChatSession`'s cancel catch is now `catch (Exception ex) when (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)`. Not checkable by hand on the emulator; the review probe showed the exception it catches.
- [x] **Thinking (note only):** Haiku 5.5 on Bedrock thinks by default; the package maps reasoning blocks to `TextReasoningContent` and keeps their signatures. The history must replay them unchanged (the week 5 `/ask` lesson). Nothing to test on the emulator.
  - **Noted.** Nothing to test on the emulator (no model, no thinking blocks).

---

## Block 6: Tests (30 min) — done

- [x] `ChatClientFactory.Create("bedrock", …)` returns an `IChatClient` whose `ChatClientMetadata` has the Bedrock provider name and the model ID. Next to `EmbeddingGeneratorFactoryTests` in `AiLab.Api.Tests`. No network.
- [x] `ModelPrices.Cost("us.amazon.nova-lite-v1:0", …)` returns a value; an unknown Bedrock ID still returns null.
- [x] `ProviderErrorClassifier` recognises an AWS service exception (`ValidationException`) and an event-stream one (`BedrockRuntimeEventStreamException`); your `AmazonClientException` choice is pinned by a test.
- [x] `dotnet test` on the whole solution: everything green.
  - **Done (handed over):** 28 new offline tests in `AiLab.Api.Tests` (175 → 203; solution 371 passed, 42 skipped, 0 failed).
    - `ChatClientFactoryTests` (13): metadata `aws.bedrock` + model ID for 3 IDs; the SDK client's real `Config` read back through `GetService<IAmazonBedrockRuntime>()`: region endpoint without a URL, `us-east-1` default, URL kept with `AuthenticationRegion` and no region endpoint (R4), timeout and retries reach the config, null keeps the SDK defaults and no HTTP factory, the emulator CA sets a factory whose `HttpClient` carries the timeout (R6), CA path without URL refused, missing CA file and a file with no certificate refused, unknown provider refused. The CA is a throwaway self-signed PEM made in the test.
    - `ModelPricesTests` (8): the 4 Bedrock IDs, the emulator turn (in 2 / out 38 = $0.00000924), and 3 unpriced IDs (`nova-pro`, the bare Haiku 5.5 ID, `eu.` Nova Lite) → null, never $0.
    - `ProviderErrorClassifierTests` (7): `HttpRequestException`, `ValidationException`, `BedrockRuntimeEventStreamException`, `TimeoutException` are provider failures; `AmazonClientException`, `InvalidOperationException` and the package's `ArgumentException` (response format + tools) are not.
    - Not covered: the `ChatSession` cancel filter (`AiLab.Console` has no test project).
  - **Found:** run in a shell that sourced `ai-lab-kb.env`, one test failed: the SDK copies `AWS_ENDPOINT_URL` into `ServiceURL` when the client is built, over our `RegionEndpoint`. So an emulator terminal always reaches the emulator, even without `Bedrock:ServiceUrl` (the safe direction; the reverse, a real-account call in that terminal, can't happen). The test now checks `ServiceURL` against the variable when it's set; all 28 pass in both shells.

---

## Block 7: Housekeeping (30 min) — done

- [x] `experiments.md` section 22: "Bedrock through `IChatClient` on aws-emulator": setup, the two turns (updates, tokens, finish reason, cost), the model-ID result, error and Ctrl+C checks, what the emulator can't show (real text, real token counts, AccessDenied on streaming without `InvokeModelWithResponseStream`).
- [x] `docker/aws-setup/README.md` → "Into the MEAI day": tick the open question (profile vs bare ID on `converse`).
- [x] `CLAUDE.md`: Week 7 Tuesday entry (third provider, `Bedrock:*` keys, how to run on the emulator, one retry layer).
- [x] `README.md`: providers list and the run command.
  - **Done (handed over):** section 22 (setup, the two turns, prices, an errors/cancel/timeout table, the ticket-analyzer gap, TLS and endpoint findings, what the emulator can't show, conclusion and decision; the Week 7 heading and table of contents renamed to cover it). aws-setup README: the profile question answered. CLAUDE.md: the `"bedrock"` provider line, `EmulatorHttpClientFactory`, resilience and classifier, Bedrock prices, the current step, a Week 7 Tue entry with one line per block and the follow-ups. README: Bedrock in "What it does", the diagram and the provider sentence, a "Bedrock chat on aws-emulator" run section, the Week 7 status.
- [x] Commit, open a PR.
  - PR #15 opened: https://github.com/alihammoud17/ai-labs/pull/15

---

## Optional: the same switch on `/ask` (20 min)
- [x] Run `AiLab.Rag` with the same `Chat__Provider=bedrock` variables and call `POST /ask` once. The answer is the emulator's canned text; the point is that the API host needed no code change.
  - **Why:** proves "the same abstraction" across hosts, not just the console.
  - **Expect (R10):** `502 reply_unreadable`, not the canned text. `/ask` uses `GetResponseAsync<AskAnswer>`; the package turns that into a forced synthetic tool, and the emulator answers it with `{}` (no `citedSources` → unreadable → one repair → still `{}`). That 502 is the wiring working, not failing. (On the real account, Haiku 5.5 supports `BedrockStructuredOutputMode.Native`; note for later.)
  - **Done (with Block 5's checks):** `us.amazon.nova-lite-v1:0` → `502 reply_unreadable`, as expected (no code change in `AiLab.Rag`); an unknown model → `502 provider_unavailable`. Run on port 5098, stopped after.

---

## End of day
- [x] D1 checked
- [ ] `docker/bedrock-kb/bedrock-kb-emulator.sh stop`
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Do 2 → 3 → 4 → 7. The prices (start of Block 5) are what make D1's cost appear; the rest of Block 5 and Block 6 can follow.
