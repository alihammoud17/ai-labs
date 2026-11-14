# Demo GIF

`milestone-1.gif` is the demo at the top of the repo's README. It's recorded with [VHS](https://github.com/charmbracelet/vhs) from the tape files in this folder, so it can be recorded again the same way after a change.

| File | What it does |
|---|---|
| `milestone-1.tape` | The main tape: settings (1000×600, font 16, bash), Haiku 5.5 through `Chat__*` env vars, then the three scenes |
| `scene-1-ask.tape` | `curl POST /ask`: an answerable question (cited answer), then one the docs don't cover (abstains) |
| `scene-2-saas.tape` | `AiLab.Console`, `/saas on`, one question answered with the SaaS API's MCP tools |
| `scene-3-agent.tape` | `/agent`, then `/workflow`, on the same webhooks ticket: steps and totals lines |
| `demo-env.sh` | Sourced, hidden, before scene 1: `$J` (the JSON header) and `$SHOW` (a jq filter for the answer, status, gate and citations), and a plain `$ ` prompt |
| `console.sh` | Starts the console from the repo root and drops the two startup lines that show local paths (`Using launch settings from …`, `Logs: <folder>`) |

Waiting for the model is hidden (`Hide` … `Wait` … `Show`), so the GIF stays under 45 seconds. Nothing is sped up or edited: the totals lines show the real model calls, tokens, cost and time.

## Re-record

1. Install VHS once: `brew install vhs` (it brings `ttyd` and `ffmpeg`).
2. Start what the scenes call, each in its own terminal, from the repo root unless noted:

   ```bash
   colima start && docker compose up -d                       # Postgres + pgvector
   dotnet run --project Presentation --launch-profile http    # in MultiTenantSaaSApi, on master, with its PostgreSQL and Redis up
   Chat__Provider=anthropic Chat__Model=claude-haiku-5-5 dotnet run --project AiLab.Rag
   curl -X POST "localhost:5074/dev/ingest?chunker=heading"   # once; re-runs embed only changed chunks
   ```

   The user-secrets the repo README lists must be set: `Anthropic:ApiKey`, `ConnectionStrings:Vectors`, `SaasMcp:Email`, `SaasMcp:Password`.
3. Build, so `console.sh`'s `dotnet run --no-build` runs the current code, then record:

   ```bash
   dotnet build AiLab.slnx
   vhs docs/demo/milestone-1.tape
   ```

**Cost:** one take is about one cent on Haiku 5.5: 2 `/ask` answers (about $0.0004 each, `experiments.md` section 17), one `/saas` question (about $0.0004–0.001, section 18), the `/workflow` run (about $0.0008) and the `/agent` run, the expensive one (about $0.007 on this ticket, section 19).

`/ask` allows 10 requests per minute per IP (`Ask:PermitsPerMinute`), so wait a minute between takes if you rehearsed with curl.

## Check before committing

```bash
ls -lh docs/demo/milestone-1.gif                                   # under 5 MB
ffprobe -v error -show_entries format=duration -of csv=p=0 docs/demo/milestone-1.gif   # 45 s or less
grep -n -i -E 'bearer|eyJ[a-zA-Z0-9_-]{10,}|sk-ant|password|apikey|@[a-z0-9-]+\.[a-z]' docs/demo/milestone-1.txt docs/demo/*.tape docs/demo/*.sh
```

`milestone-1.txt` holds every recorded frame as text (git-ignored), so the last command checks what's on screen for tokens, keys, passwords and email addresses. It should print nothing from the `.txt` file. Then watch the GIF once: the scenes run live, so an answer can differ from the last take.
