# Workflows vs Agents, Then Build One

- **Date:** Fri 13 Nov 2026 (week 6, day 4)
- **Repo:** ai-labs
- **Branch:** `stage-6-first-agent` (from `main`; use the repo's usual branch naming)
- **Status:** planned
- **Goal for today:** understand the difference between a workflow and an agent, then build one of each and compare them
- **Time:** about 4.5–5.5 hours. Blocks 1–5 and 7 are the minimum; Block 6 is extra.

How we work: I type the commands and write the code. Claude explains each step, checks my work (build, tests) and ticks the boxes. Claude only writes the code for a step when I say "hand it over". I use llama3.2 on Ollama while building (it's free) and Haiku only for the recorded comparison in Block 5.

## Deliverables
- [ ] **D1. Notes:** the agent loop, and when a workflow beats an agent (kept outside the repo, for the recap PDF)
- [x] **D2.** A Microsoft Agent Framework agent built on my `IChatClient`, using the stage 3 tool (`TicketTools.GetRecentChanges`) and the SaaS API's MCP tools

## Resources
- Anthropic, Building Effective AI Agents: https://www.anthropic.com/engineering/building-effective-agents
- Agent Framework .NET getting-started samples: https://github.com/microsoft/agent-framework/blob/main/dotnet/samples/01-get-started
  - `02_add_tools` (giving an agent functions), `03_multi_turn` (keeping a conversation with `AgentSession`), `05_first_workflow` (steps linked together)
- Generative AI for Beginners .NET, lesson 4: https://github.com/microsoft/Generative-AI-for-beginners-dotnet
- In the repo: `TicketTools` and `TicketAnalyzer` (stage 3), `AiLab.Infrastructure/Mcp/` and the `/saas` command (Wednesday), `experiments.md` sections 7 and 18

## What we're building
A helper that handles **support tickets** for my SaaS. Someone writes "our webhooks stopped arriving since yesterday" or "the new frontend gets CORS errors", and before answering, the helper looks things up:
- `GetRecentChanges(component)`: what was deployed recently (the stage 3 function, over the fake change log; `AIFunctionFactory` keeps the method name, so this is the name the model sees). The log knows only `billing`, `invoices`, `auth` and `tenants`, all October changes; any other component gets "Unknown component …" with that list.
- `get_usage_summary`, `list_audit_logs`, `list_webhook_endpoints`, `list_cors_origins`: the tenant's real data from the SaaS API (read-only; the tenant comes from the login, not from the question)

Its answer always has four parts: a short summary, what it found (which lookup returned what), the likely cause or a plain "I don't know", and what to do next.

I build this twice:
- as an **agent**: the model decides which lookups to make, in what order, and when it has enough
- as a **workflow**: my code decides the steps, and the model only fills in the parts that need language

Then I run both on the same tickets. The results are what the "when a workflow beats an agent" part of my notes is based on.

---

## Block 1: Read and start the notes (60 min) → D1 first draft

- [x] Read "Building Effective AI Agents" from start to finish
- [x] Look through the Agent Framework samples `01`–`03` and `05`, and lesson 4 of the course
- [x] Start the notes outside the repo, under these headings (I finish them in Block 7 with today's results):
  1. **The starting point:** a model that can call functions, search documents and remember the conversation
  2. **Workflows**, where the steps are written in code. The article names five common shapes:
     - a chain of calls, each one working on the last one's output
     - sorting the request first, then sending it down the right path
     - several calls at the same time, results combined
     - one call splitting the job up and handing out the pieces
     - one call writing, another reviewing, repeated until it's good enough

     One line each, with an example from my own projects where I can find one.
  3. **Agents**, where the model decides the steps. **The agent loop** as a drawing: the goal → the model picks an action → my code runs the function → the model reads the result → it picks again → it stops. What makes it stop: it has an answer, it hits the step limit, it runs out of budget, or a person has to approve something.
  4. **When a workflow beats an agent:**
     - the steps are known in advance
     - cost and response time have to be predictable
     - it has to be easy to test and to find what went wrong
     - every step has to be traceable
     - the model is small

     **When an agent is worth it:**
     - the problem is open-ended
     - the next step depends on what the last lookup returned
     - a few wasted steps don't cost much
  5. **What can go wrong with agents:** one early mistake carries through every step after it, costs climb, it can go round in circles, it can use a function in the wrong way. How to protect against it: clear function descriptions, a step limit, read-only functions, and asking a person before anything that changes data.
- [x] Answer this in your own words: which features I've already built are workflows? (`TicketAnalyzer`: one call, one function, one retry if the reply is broken. `/ask`: search → check → write → check the sources.)

---

## Block 2: Check the design (20 min)

- [x] Confirm these Agent Framework basics against the samples and the source:
  - The package is `Microsoft.Agents.AI` (1.28.0). It needs `Microsoft.Extensions.AI` 10.11.0 or newer, which the repo already uses. It also pulls `Microsoft.Extensions.VectorData.Abstractions` 10.10.0 (the console gets 10.8.2 through Infrastructure today), one more reason to keep it in the console only.
  - `IChatClient.AsAIAgent(instructions, name, description, tools)`, or `AsAIAgent(ChatClientAgentOptions)`, gives a `ChatClientAgent`. Only the options overload can set `UseProvidedChatClientAsIs`.
  - `agent.RunAsync(...)` / `RunStreamingAsync(...)`, and `AgentSession session = await agent.CreateSessionAsync()` to keep a conversation going. The session holds its own history (in memory), separate from `ChatConversation`.
  - Streaming gives `AgentResponseUpdate`s: `Contents` and `Text` like a `ChatResponseUpdate`, but **no `Usage` property**; tokens arrive as `UsageContent` items in `Contents`.
  - **By default the agent wraps the chat client in its own layers** (`WithDefaultAgentMiddleware`): two approval layers, an OpenTelemetry layer and the one that runs functions (`FunctionInvokingChatClient`). It adds that last one **only if** `chatClient.GetService<FunctionInvokingChatClient>()` finds none, so two function layers can't happen by accident. What can go wrong:
    - plain provider client + `UseProvidedChatClientAsIs = true` → **no** function layer: tool calls come back unexecuted
    - plain provider client + defaults → the framework's function layer with its own defaults (40 iterations), not my settings
    - my own client + defaults → the extra approval and OpenTelemetry layers I don't need, and `AllowConcurrentInvocation` would change a setting on my function layer

    So: a client I set up myself, with `UseProvidedChatClientAsIs = true`.
- [x] Agree on these choices (a starting point; check them against the repo first):
  - **Chat client:** a second pipeline of my own, separate from the console's DI `IChatClient` (so `/saas` keeps its limit of 3): `new ChatClientBuilder(keyed provider).UseFunctionInvocation(MaximumIterationsPerRequest = Agent:MaxToolIterations, IncludeDetailedErrors = false).UseLogging().Build(services)`, with `UseProvidedChatClientAsIs = true`.
    - The first `Use…` is the outermost layer, so function invocation wraps logging and logging records every call to the model (same order as `Program.cs`). The other order would log one call per run.
    - `Build(services)`: without the service provider `UseLogging` finds no logger factory and logs nothing.
    - `Agent:MaxToolIterations` (default 6) in the console's `appsettings.json` is the rule that stops the agent; the step-limit test in 3.5 overrides it with `Agent__MaxToolIterations=1`, no code change.
  - **Where the code goes:**
    - the agent's instructions text in `AiLab.Application` (`Agents/TriageAgentInstructions`)
    - creating the agent and the `/agent` command in `AiLab.Console`, the only project that has both the provider client and `SaasMcpToolSource`
    - the `Microsoft.Agents.AI` package in the console project only

    Domain still has no packages.
  - **Functions:** `TicketTools.AsAIFunction()` (its component list comes from the change log) plus the 4 `McpClientTool`s from the agent's **own** `SaasMcpToolSource`. The registered one is a singleton shared with `/saas`: if `/agent` connected it, a later `/saas on` would see `IsConnected`, print "already on" and never give the chat its tools, and `/saas off` would cut the agent's tools mid-session. So the agent gets a second instance (`ActivatorUtilities.CreateInstance<SaasMcpToolSource>(sp)`; the token provider stays shared, so its cached tokens are reused), disconnected on `/agent reset` and on exit.
  - **Reply length:** `MaxOutputTokens = 2000`. The default of 1,024 cut Haiku off on Wednesday.
- [x] Check which components the fake change log knows (`InMemoryChangeLogRepository`), so the Block 5 tickets have something to find (checked: `billing` v4.18.0 explains ticket A, `invoices` v4.19.0 ticket C; `auth` and `tenants` are noise; nothing for webhooks, CORS or usage)

---

## Block 3: Build the agent (75 min) → D2

### 3.1 Add the package
```bash
dotnet add AiLab.Console package Microsoft.Agents.AI --version 1.28.0
dotnet build
dotnet test
```
- [x] The build passes and every test still passes (the package moves the console to `VectorData.Abstractions` 10.10.0; note it)

### 3.2 Write the instructions
- [x] `TriageAgentInstructions` says:
  - its job: handling support tickets for the SaaS
  - look things up **before** naming a cause
  - the data only covers the logged-in tenant
  - say "I don't know" when the lookups don't show a cause
  - answer in four parts: summary, what was found, likely cause or "I don't know", next step
  - **today's date**, so "since yesterday" or "Tuesday's release" can be matched to the change dates (as `TicketAnalyzer` does with `TimeProvider`). So the instructions are a method that takes the date and returns the text, called once per session.

  **Done (handed over):** `AiLab.Application/Agents/TriageAgentInstructions.cs`, a static `Build(DateOnly today, IReadOnlyList<string> components)` returning plain text (the console streams it, so no JSON). It covers every point above. The date includes the weekday ("2026-11-13 (Friday)") so "Tuesday" can be resolved without date arithmetic, and the component list is passed in from `IChangeLogRepository.GetComponents()`, as in `TicketAnalyzer`. One extra line from Wednesday's run: leave out optional parameters instead of sending `""` (llama3.2's empty dates broke `list_audit_logs`). 7 offline tests in `AiLab.Api.Tests/TriageAgentInstructionsTests.cs` (date and weekday, components, the four headings, "I don't know"). 385 tests: 343 pass, 42 skipped (the eval and Postgres tests that need `AILAB_EVAL` / `AILAB_PG`).

### 3.3 Create the agent
- [x] Set up the chat client as agreed in Block 2, then call `AsAIAgent(new ChatClientAgentOptions { Name = "triage", Description = …, ChatOptions = new() { Instructions = …, Tools = [...], MaxOutputTokens = 2000 }, UseProvidedChatClientAsIs = true })`
- [x] Functions: `TicketTools.AsAIFunction()` plus the MCP functions, from the agent's own `SaasMcpToolSource` instance (Block 2), connected when the first `/agent` session starts. Never the singleton `/saas` uses.
- [x] `Agent:MaxToolIterations` (6) added to the console's `appsettings.json`

  **Done (handed over):** `AiLab.Console/Agents/TriageAgentFactory.cs`, a singleton registered in `Program.cs` (nothing calls it until 3.4). Its constructor builds the agent's own pipeline once, `new ChatClientBuilder(keyed provider).UseFunctionInvocation(cap Agent:MaxToolIterations, IncludeDetailedErrors = false).UseLogging().Build(services)`, and creates its own `SaasMcpToolSource` with `ActivatorUtilities.CreateInstance` (the token provider stays the shared singleton). `CreateAsync()` connects that MCP client (a stopped SaaS API or a bad login fails here), builds the instructions with today's date from `TimeProvider` and the change log's components, and returns `AsAIAgent(ChatClientAgentOptions { Name = "triage", Description, ChatOptions { Instructions, Tools = [GetRecentChanges, 4 MCP tools], MaxOutputTokens = 2000 }, UseProvidedChatClientAsIs = true })`. `DisconnectAsync()` closes the agent's MCP client; `DisposeAsync()` also disposes the pipeline. `Agent:MaxToolIterations` 6 in the console's `appsettings.json`. Build 0 warnings; 385 tests (343 pass, 42 skipped); the console starts in Development (DI validation passes) with the same welcome line.

### 3.4 Add the `/agent` command
- [x] `/agent <text>` sends the text to the current agent session (creating one, and connecting the agent's MCP client, if there's none) with `RunStreamingAsync`, and prints the answer as it arrives. A follow-up is just another `/agent <text>`.
- [x] `/agent reset` (the exact word only) ends the session and disconnects the agent's MCP client; `/agent` alone prints the usage and whether a session is open
- [x] **Show each step of the loop** as it happens, as `[step n: function name {arguments}]`, the same way `/saas` prints function calls. At the end, show the totals: calls to the model, function calls, tokens in and out, cost.
  - `AgentResponseUpdate` has no `Usage`: count model calls and tokens from the `UsageContent` items in `Contents` (about one per model call), function calls from the `FunctionCallContent` items, and price them with `ModelPrices`
  - check the counts once against the log file, which records every call
- [x] Ctrl+C stops a run cleanly (remember Wednesday's OllamaSharp problem)
- [x] Nothing else in the console changes when `/agent` isn't used
- [x] The build passes; `dotnet test` passes

  **Done (handed over):** `AiLab.Console/Agents/AgentCommands.cs` (registered in `Program.cs`; `ChatSession` gained one dispatch block, a constructor parameter, `/agent` in the welcome line and a dispose at exit). The first `/agent <text>` calls `TriageAgentFactory.CreateAsync` and `CreateSessionAsync` inside `TurnCancellation` and prints the tool names; every `/agent <text>` runs `RunStreamingAsync(text, session)`. Steps print as `[step s.c: name {args}]`: `s` is the model call that asked for tools (what `Agent:MaxToolIterations` counts), `c` the call within it, since one model call can ask for several functions (`[step s.c: name failed]` when a tool threw). Totals as `[agent: N model calls (S steps with tools), M function calls, T s]` plus the chat's `UsageLine`. (First version numbered tool calls only; changed after 3.5 showed llama3.2 asking for 4 lookups in one step.) Model calls = tool-result batches + 1 (the function layer sends one batch per round trip); tokens from `ToChatResponse()` over the updates, priced by `SessionCostTracker`, so agent runs count in the session total. Cancel or error → new session, same agent (the session may hold a half answer or a tool call without its result). Smoke test on llama3.2 (admins logged out earlier, not a Block 5 ticket): session started with 5 tools, 2 model calls / 1 function call, 1,484 in / 192 out, 6.5 s; the log has exactly 2 `GetStreamingResponseAsync` calls. `/saas on` afterwards still got its own 4 tools. Ctrl+C checked by hand (piped input can't send it). Model behaviour seen: llama3.2 looked up `tenants` instead of `auth`, then wrote three more tool calls as JSON text instead of calling them, and named an unrelated cause (v4.15.3).

### 3.5 First tries (llama3.2, with the SaaS API running)
- [x] *"Our webhooks stopped arriving since yesterday."* → it should check the change log and `list_webhook_endpoints` before answering
- [x] *"Is anything wrong?"* (vague on purpose) → count the steps, and check whether it stops by itself
- [x] Run with `Agent__MaxToolIterations=1` and ask again: what happens to the answer when it hits the limit?
- [x] ✅ **D2 done** (the console's printed transcript of runs with the steps, instead of a screenshot)

  **Done:** with the default limit of 6, llama3.2 stopped by itself after one step every time (the log has no "Reached maximum iteration count"). With `Agent__MaxToolIterations=1` (log: `Reached maximum iteration count of 1`, then one more model call without tools): on "Is anything wrong?" it looked up `auth`, then its whole answer was its next lookup (`billing`) written as JSON text, 35 tokens, twice in a row; on the webhooks ticket it used the real data (`a-invoices` inactive) but invented "was active before", told support to check other tenants (against the tenant-only rule), gave 2 of the 4 parts and never looked at the change log. Found on the way: the limit counts model calls that ask for tools, not tool calls (one step held 4 calls, `list_webhook_endpoints` three or four times with the same arguments), and an earlier webhooks run said the endpoint list was empty when it wasn't. Faithfulness varies run to run on llama3.2.

---

## Block 4: The same job as a workflow (45 min)

Plain C#, no loop: my code decides every step, and the model only does the writing.
The code sits next to the agent in `AiLab.Console`, so the two are easy to compare. Model calls go through the console's DI `IChatClient`: no tools are sent, so its function layer just passes them through.
- [x] **Step 1, sort:** one call to the model that returns a typed result with two separate fields (no area is a change-log component):
  - `component`: one of `IChangeLogRepository.GetComponents()`, or none
  - `area`: webhooks / CORS / usage / unknown

  Parse it with `StructuredReply.TryParse<T>` (Haiku may fence JSON), not `GetResponseAsync<T>` alone.
- [x] **Step 2, collect:** my code calls the right functions directly: `GetRecentChanges(component)` if there is a component, plus the matching SaaS lookup (`list_webhook_endpoints`, `list_cors_origins` or `get_usage_summary`) if there is an area, through `tool.InvokeAsync(...)` on the `McpClientTool`. A field that's none / unknown skips its lookup. The model isn't involved in this step (the SaaS API still audits each call as `MCP_TOOL_CALLED`).
- [x] **Step 3, write:** one call to the model writes the answer from what was collected, in the same four parts as the agent
- [x] `/workflow <ticket text>` prints each step and its token counts, like `/agent` does
**Done (handed over), with my choices (manual mode):** code in the console as planned, the model writes all four parts (a like-for-like comparison with the agent), its own MCP connection per run, an unreadable sort falls back to unknown (no repair call). `AiLab.Console/Workflow/`: `TicketRoute` (`Component` string checked against `GetComponents()`, `Area` a `TicketArea` enum serialized lowercase, so the schema lists webhooks / cors / usage / unknown) and `TicketAreas.LookupFor` (area → SaaS tool and fixed arguments; usage = 7 days); `TicketWorkflow` (`SortAsync`: `GetResponseAsync<TicketRoute>(useJsonSchemaResponseFormat: false)` + `StructuredReply.TryParse`, an unknown component dropped and shown as such; `CollectAsync`: `TicketTools.GetRecentChanges` called directly and the MCP tool through `McpClientTool.CallAsync` (typed `CallToolResult`, text blocks, `IsError`) on a fresh `SaasMcpToolSource` disposed after the call, a failing lookup becomes a failed finding; `WriteAsync`: streamed, same four headings and rules as the agent, today's date); `WorkflowCommands` (`/workflow <text>`, stateless): `[step 1 sort: component=…, area=…]` + tokens, `[step 2 collect: lookup → n lines | failed]`, the streamed answer, `[step 3 write]` + tokens, `[workflow: 2 model calls, n lookups, in, out, $, s]`; Ctrl+C with the OllamaSharp check; cost in the session total. Smoke test on llama3.2 (not Block 5 tickets): always 2 model calls. With `Area` as a string, a CORS question came back as component=cors, area="allowed origins" and the check dropped the lookup, so `Area` became an enum (my choice); after that the same question sorted to cors 2 of 3 runs (once unreadable, and once webhooks in another run). "Hello, how are you?" still got a pointless change-log lookup (auth, then billing). On the admins ticket llama3.2 found v4.17.1 but answered "I don't know". Build 0 warnings, 385 tests (343 pass, 42 skipped).
- [ ] In the notes, write down which of the article's shapes this is (sorting first, then a chain of calls)

---

## Block 5: Compare the two (45 min)

The tickets, matched to the change log (only `billing`, `invoices`, `auth`, `tenants`, all October) and to tenant A's data (`experiments.md` section 18). The change log never deployed anything on Tue 10 Nov, so the old "after Tuesday's release" ticket had nothing to find; T4 now uses ticket A's text, where the change log holds the cause. I don't add webhook or CORS entries to the fake log: they would change `TicketAnalyzer`'s component list and could move the B/D eval results and tests.

| # | Ticket | What the lookups can show | Which should do better, and why |
|---|---|---|---|
| T1 | "Our webhooks stopped arriving since yesterday." | Change log: nothing ("Unknown component"). `list_webhook_endpoints`: `a-orders` active, `a-invoices` inactive | Workflow: the path is obvious |
| T2 | "The new frontend at https://app.example.com gets CORS errors." | Change log: nothing. `list_cors_origins`: only `https://app.company-a.com`, so the new origin isn't allowed | Workflow |
| T3 | "API usage looks weird this week." | Change log: nothing. `get_usage_summary` only | Either |
| T4 | Ticket A: "We upgraded from Starter to Pro on Tuesday. The preview said $12.34 but the card was charged $12.35, same on 3 other upgrades this week." | `billing` v4.18.0: proration now rounds up, plan-change preview not updated | Agent: it has to connect the ticket to the change log |
| T5 | A ticket none of the lookups can explain | Nothing | Both should say "I don't know" |
| T6 (optional) | Ticket C: "Since last night our new invoices start from INV-ACME-0001 again. Our last one was INV-004512." | `invoices` v4.19.0: per-tenant numbering | Either |

Also watch how each design handles the change log's "Unknown component" reply on T1–T3.

- [x] Write down what I **expect** for each ticket **before** running it (the repo's rule)
- [x] Run every ticket through `/agent` and `/workflow`, first on llama3.2, then on Haiku for the recorded run
  **Done (handed over):** 3 runs per ticket per design on each model, each a fresh console process (run script and transcript parser kept in the session scratchpad, not the repo); transcripts in `eval-runs/2026-11-14-agent-vs-workflow/` (git-ignored). T5 run as T5a (slow search) and T5b (wrong logo, the v4.17.0 trap), T6 added. Expected written and committed before any run. Haiku $0.0817 (agent $0.0681, workflow $0.0136); 4 of 21 Haiku agent answers cut off at 2,000 output tokens after `list_audit_logs` with pageSize 100–200 (setup kept after run 1, my choice). Numbers in `experiments.md` section 19; verdicts, "what I noticed" and the conclusion are mine.
- [ ] For each run, note:
  - calls to the model and function calls
  - tokens in and out, cost, time taken
  - my own verdict: right / partly right / wrong / honest "I don't know"
- [ ] Add a new section to `experiments.md`: setup, what I expected, results table, what I noticed, and a conclusion ("a workflow beats the agent when …")

---

## Block 6: Extra (optional, 45 min)

- [ ] Rebuild the workflow with Agent Framework **Workflows** (`WorkflowBuilder`, steps linked together, as in sample `05`) and compare the code with the plain C# version
- [ ] **Asking before acting:** read how Agent Framework handles functions that need someone's approval, and write down how a future function that changes data (e.g. "disable a webhook endpoint") would ask for a yes/no in the console
- [ ] Give the agent `AiLab.McpServer`'s functions too, through a second MCP connection (one connection per server)

---

## Block 7: Docs, notes and PR (30 min)

- [ ] Finish the D1 notes with today's results: the comparison table, the loop as I saw it printed, and the step-limit test ✅ **D1 done**
- [x] `CLAUDE.md`:
  - a Week 6 Friday entry (plan, branch, results)
  - the Console line (`/agent`, `/workflow`), the new package (and the `VectorData.Abstractions` 10.10.0 it brings), the chat client choice (`UseProvidedChatClientAsIs`, its own pipeline, `Agent:MaxToolIterations`) and the agent's own MCP connection
  - tick Monday's item if its Block 5.2 is done
- [x] `README.md`: a "Support ticket agent" section (how to run `/agent` and `/workflow`, and what each one prints)

  **Done (handed over):** `CLAUDE.md`: `TriageAgentInstructions` in the Application line, `/agent` and `/workflow` in the Console line, two new lines after the Console host line (the triage agent: package and its `VectorData.Abstractions` 10.10.0, own pipeline, `UseProvidedChatClientAsIs` and why, own MCP connection, step labels, usage from `ToChatResponse`; the workflow: sort / collect / write), the current step, and a Week 6 Friday entry with results and follow-ups. Monday's item left unticked (its Block 5.2 is your notes, not confirmed). `README.md`: a "What it does" bullet, the two commands in the table, a "Support ticket agent" section (both commands with real output from the Haiku run, the step labels, the pipeline and session, the comparison's numbers), the project structure, three "What I learned" bullets, week 6 cost ($0.085, total about $1.13) and Status.
- [ ] Commit at the end of each block; open the PR

---

## End of day
- [ ] Both deliverables checked
- [ ] Ask Claude for today's recap PDF (for OneDrive), with the D1 notes in it

> **Short on time?** Do Blocks 1 → 2 → 3 → 4 → 5 (llama3.2 only, Haiku another day) → 7. Skip Block 6 first.