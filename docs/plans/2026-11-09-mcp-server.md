# MCP Day Plan: Hosts, Clients & Servers

- **Date:** Mon 9 Nov 2026
- **Branch:** `stage-6-mcp-server` (from `main`)
- **Status:** in progress
- **Focus:** How MCP hosts, clients and servers fit together
- **Where:** ai-labs, as a new presentation host `AiLab.McpServer` in `AiLab.slnx`
- **Time left:** ~2.5–4 h (Blocks 2, 3 and 5 are the minimum; Block 4 is optional)

Tutor mode: I type the commands and write the code; Claude explains each step, reviews it (build, tests) and ticks the boxes. Claude writes a step's code only when I say "hand it over".

## Deliverables
- [ ] **D1. Notes:** host/client/server, tools/resources/prompts, stdio vs HTTP transport
- [ ] **D2. Quickstart:** `AiLab.McpServer` running and called from Claude Code

## Resources
- modelcontextprotocol.io, What is MCP?: https://modelcontextprotocol.io/docs/getting-started/intro
- Microsoft Learn, Minimal MCP server in C#: https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server
- Anthropic Academy, Intro to MCP (concepts only, skip the Python): https://anthropic.skilljar.com/introduction-to-model-context-protocol
- Claude Code MCP docs: https://code.claude.com/docs/en/mcp

## Scope rules for today
- `AiLab.McpServer` has **no project references** today. The quickstart tools stand alone.
- Later it will reference Application + Infrastructure and expose existing services (e.g. `IAskService` → `ask_corpus`, vector search → `search_corpus`) as thin tool adapters. **Not today.**
- No NuGet publishing (quickstart sections 8–10).

---

## Block 1: Concepts, kept short ✅ DONE

- [x] **Host**: the app the user works in (Claude Code, VS Code, Claude Desktop).
- [x] **Client**: lives inside the host, with **one client per server** (3 servers means 3 clients).
- [x] **Server**: a separate program that offers capabilities.
- [x] Who controls each primitive?
  - **Tools**: the *model* decides when to call them (the user approves). They can have side effects.
  - **Resources**: the *app or user* attaches them as context. They're read-only.
  - **Prompts**: the *user* picks them as templates (they show up as `/mcp__<server>__<prompt>` slash commands in Claude Code).
- [x] Messages: JSON-RPC 2.0. An `initialize` request and response exchange capabilities, then the client sends an `initialized` notification.

---

## Block 2: Build the server (60–75 min)

### 2.1 Prerequisites
- [x] `dotnet --version` shows 10.x (the .NET 10 SDK is required)
- [x] `node --version` works (only needed for the MCP Inspector, which runs via `npx`)

### 2.2 Create the project (from the ai-labs root)
```bash
dotnet new install Microsoft.McpServer.ProjectTemplates
dotnet new mcpserver -n AiLab.McpServer
dotnet sln AiLab.slnx add AiLab.McpServer/AiLab.McpServer.csproj
dotnet build
```
- [x] The **whole solution** builds and the existing tests still pass (`dotnet test`)
- [x] The `.csproj` matches the other projects (there's no `Directory.Build.props`, each project sets its own): `net10.0`, `Nullable` and `ImplicitUsings` enabled, `Microsoft.Extensions.Hosting` at 10.0.13
- [x] Remove the template's packaging settings from the `.csproj` (`PackAsTool`, `PackageId` and the other package metadata, self-contained `RuntimeIdentifiers`) and its publishing files (the project's own `README.md`, `.mcp/server.json`, if present). You won't publish today.

### 2.3 Read the code
- [ ] `Program.cs`: find `WithStdioServerTransport()` (registers the **stdio transport**) and `WithToolsFromAssembly()` (**finds the tools**)
- [ ] Also in `Program.cs`: find the logging setup that sends logs to **stderr** (you'll see why in Block 3)
- [ ] `Tools/RandomNumberTools.cs`: note that `[McpServerTool]` and `[Description]` are what the model reads, so descriptions work like prompt text

### 2.4 Add a tool
- [ ] Add `GetCityWeather` (from section 5 of the quickstart) to `RandomNumberTools.cs`. It reads the `WEATHER_CHOICES` env var. The SDK exposes it to the model as `get_city_weather`.
- [ ] `dotnet build` succeeds

### 2.5 Watch the raw protocol with the MCP Inspector (optional, very useful)
From the ai-labs root:
```bash
npx @modelcontextprotocol/inspector dotnet run --project AiLab.McpServer/AiLab.McpServer.csproj
```
- [ ] Connect, list the tools, call `get_random_number`
- [ ] Look at the JSON-RPC request/response pairs (`initialize`, `tools/list`, `tools/call`)

---

## Block 3: Call it from Claude Code (30–45 min) → D2

### 3.1 Register the server (local scope)
From the **ai-labs root**. All options go **before** the server name, and everything after `--` is the server command:
```bash
dotnet build AiLab.McpServer
claude mcp add --env WEATHER_CHOICES=sunny,humid,freezing --transport stdio ailab-mcp \
  -- dotnet run --no-build --project "$PWD/AiLab.McpServer/AiLab.McpServer.csproj"
claude mcp list
```
> `--no-build` because a build inside `dotnet run` can print messages to stdout and break the stdio stream (the 3.3 rule). Run `dotnet build AiLab.McpServer` yourself after every change.
> Keep `--transport stdio` between `--env` and the name: if the name comes right after `--env`, the CLI reads it as another `KEY=value` pair and rejects it.

### 3.2 Call it
- [ ] Start `claude` from the ai-labs root, run `/mcp`: `ailab-mcp` shows **connected** and lists its tools
- [ ] Prompt: *"Give me a random number between 1 and 100"* → approve → tool is called
- [ ] Prompt: *"What's the weather in Beirut?"* → approve → random weather from `WEATHER_CHOICES`
- [ ] Screenshot of Claude Code calling your tool ✅ **D2 done**

### 3.3 Break it on purpose (stdout rule)
- [ ] Add a `Console.WriteLine` inside a tool, rebuild, call the tool again: watch the connection break
- [ ] Remove it and confirm everything works again
> With stdio, stdout is reserved for protocol messages. Logs must go to stderr.

### 3.4 Try project scope (how a team shares server configs)
- [ ] Remove the local entry first, since local scope wins over project scope: `claude mcp remove ailab-mcp`
- [ ] Re-add it with `--scope project` (before the name, like the other options)
- [ ] Open the generated `.mcp.json` at the repo root
- [ ] Replace the absolute Mac path in it with `${CLAUDE_PROJECT_DIR:-.}/AiLab.McpServer/AiLab.McpServer.csproj` so the file works on any machine
- [ ] Restart `claude` and approve the project-scoped server when asked
- [ ] Commit `.mcp.json` **only if** the server connects with that path (it's not certain Claude Code expands `CLAUDE_PROJECT_DIR` for MCP servers; it's documented for hooks). Otherwise don't commit it.
> A committed `.mcp.json` makes every Claude Code session in this repo ask to approve `ailab-mcp`.

**Troubleshooting**
- First connection fails or times out? The server must be built first (`--no-build`): run `dotnet build AiLab.McpServer`, then retry.
- Claude answers without calling the tool? Improve the `[Description]`, or name the tool in your prompt.

---

## Block 4: Stretch, try HTTP transport (45 min, optional)

Do this as a **throwaway project outside the repo**. It's for comparing transports, not for keeping.
- [ ] `dotnet new mcpserver --help` → find the transport option
- [ ] Create a scratch HTTP server in a temp folder (e.g. `~/scratch/HttpMcp`)
- [ ] Compare its `Program.cs` with `AiLab.McpServer`'s: HTTP transport + `MapMcp()`. It's an ASP.NET Core app, not a child process.
- [ ] Run it, get the address from the generated `.http` file
- [ ] `claude mcp add --transport http scratch-http http://localhost:<port>/`
- [ ] `/mcp` shows it connected, and a tool call works
- [ ] Remove it afterwards: `claude mcp remove scratch-http`
- [ ] Bonus (in `AiLab.McpServer`): add one **resource** and one **prompt** so you've used all three primitives yourself

---

## Block 5: Housekeeping + notes (45–60 min)

### 5.1 Repo housekeeping
- [ ] `CLAUDE.md`: "ten projects" → eleven, and add an `AiLab.McpServer` line (presentation host, stdio MCP server, no references yet)
- [ ] `README.md`: add `AiLab.McpServer` to the Presentation list in Project structure, reword "Each `Program.cs` calls `AddInfrastructure()` and `AddApplication()`" so the MCP server is the exception, add a line to the project tree, plus a short "Running the MCP server" note (the `claude mcp add` command)
- [ ] Commit and open a PR

### 5.2 Notes → D1 (outside the repo)
Write them from what you saw, not from the docs.
- [ ] **1. Architecture.** Diagram: host → client (one per server) → server, using Claude Code + `AiLab.McpServer` as the example
- [ ] **2. Primitives table**

  | Primitive | Controlled by | Side effects? | .NET example |
  |---|---|---|---|
  | Tools | Model (user approves) | Yes | `[McpServerTool]` |
  | Resources | App / user | No (read-only) | |
  | Prompts | User | No | |

- [ ] **3. Lifecycle:** `initialize` → `initialized` → `tools/list` → `tools/call` (use what you saw in the Inspector)
- [ ] **4. stdio vs HTTP**

  | | stdio | Streamable HTTP |
  |---|---|---|
  | Process | Host starts the server as a child process | Server runs on its own; many clients can connect |
  | Where | Local only | Local or remote |
  | Auth | Credentials come from env vars (inherits your machine's trust) | Optional in the spec, but expected for remote servers; the spec defines OAuth 2.1 |
  | Gotcha | Never write to stdout | Network, auth, CORS (for browser clients) |
  | Use when | Personal dev tools, CLIs | Shared/hosted servers, SaaS integrations |

- [ ] **5. My use case (1 paragraph):** how `AiLab.McpServer` will expose ai-labs services (`ask_corpus`, `search_corpus`), and how MCP could fit the multi-tenant SaaS API. This is the part to talk about in interviews.
- [ ] ✅ **D1 done**

---

## End of day
- [ ] Both deliverables checked
- [ ] Ask Claude for today's recap PDF (for OneDrive)

> **Short on time?** Do Blocks 2 → 3 → 5. Drop Block 4 first.
