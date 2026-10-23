# Failure table: `POST /analyze-ticket`

Potential production failures of `AiLab.Api`, based on the code as of 2026-10-23 (end of stage 3).
**Detected by** says how each failure would be noticed today, using only what the code already does:
a status code, a log line, the diagnostics in the response, or nothing.

## 1. Provider and network

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 1 | Provider unreachable | Ollama down, DNS or network fault, Anthropic outage | Retries, then 502 | **502** + warning log "Model provider call failed" | `OllamaUrl` is still `localhost` |
| 2 | Rate limited (429) | Traffic spike, low Anthropic tier | SDK retries, then 502 | **502** + warning log. The SDK's own retries aren't logged | No rate limit on our side |
| 3 | Slow outage on Anthropic | Anthropic answering slowly or timing out | Each request waits up to 60 s × 3 attempts | **502/504** in the end + log. Slowness itself: **nothing** | No circuit breaker for Anthropic |
| 4 | Ollama overloaded | Concurrent requests on one GPU | Queue wait → timeout → retry adds load | **502** + Polly warning logs. Queue depth: **nothing** | Retry storm |
| 5 | Model retired or renamed | Provider retires the model ID | 404 → 502 on every request | **502** + log (the 404 shows up only in the logged exception) | Looks like a short outage |
| 6 | Silent model update | Provider changes model behaviour | More parse failures | **Nothing** (no 502-rate tracking) | No trend metric |

## 2. Timeouts and cost

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 7 | Budget math doesn't add up | 4 model calls × up to 180 s each | 504 at 240 s | **504** status only. The startup warning doesn't fire with the current values | Slow calls plus retries use most of the budget |
| 8 | Proxy cuts the connection first | nginx or ALB 60 s, App Service 230 s | Client gets the proxy's 502/504 | **Proxy logs** only. Server: **nothing** useful | Server may keep paying |
| 9 | Billed retries | A timed-out attempt still generated tokens | Paid for, then repeated | **Nothing** (failed attempts aren't in `Usage`) | Cost of failures isn't visible |
| 10 | Output truncated | Thinking uses up `MaxOutputTokens` 2000 | Cut-off JSON → 502 | **502** + warning log with the raw reply, **logged as a parse failure** | `FinishReason` isn't checked or logged |
| 11 | No spending cap | Abuse or a loop | Unlimited | Cost per request in **diagnostics** + info log. Totals: **nothing** | No budget, no total |

## 3. Model output quality

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 12 | **Made-up cause** | Model names a change that doesn't exist or doesn't fit | 200, wrong answer | **Nothing** (only the offline eval) | No check against the tool output |
| 13 | Answer without the tool | Model skips `GetRecentChanges` | 200, not based on the change log | **Diagnostics** `toolCalls: []` + info log, but nothing alerts on it | No rule acts on it |
| 14 | Valid JSON, missing fields | `summary: null`, 0 or 12 test cases | 200 with nulls | **Nothing** | No checks after parsing |
| 15 | Wrong shape | `testCases` sent as a string, extra wrapper | 502 | **502** + warning log with the raw reply | No retry or repair |
| 16 | Different answer each time | Temperature 1.0 | Different cause per call | **Nothing** (only eval runs) | Users see inconsistent answers |

## 4. Tool and data

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 17 | Bad tool arguments | No component passed (Llama) | 3 failures in a row → 502 | **502** + warning log "Tool loop failed" | Handled |
| 18 | Real repository throws | DB error or timeout | 3 in a row → **500**. A single failure goes back to the model | **500** + error log. Single failures: **nothing** for the caller | Only `ArgumentException` is mapped |
| 19 | Unbounded tool result | Every change is returned | Tokens grow | Token counts in **diagnostics** and log, but no alert | No limit or time window |
| 20 | Model doesn't know the date | No date in the prompt | "Recent" means nothing | **Nothing** | Wrong causes look plausible |
| 21 | `Component` not validated | `"foo"` or a long string | Goes into the prompt | **Nothing** at validation. The info log shows it only if the model passes it to the tool | Not checked against `GetComponents()` |

## 5. Input and security

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 22 | **No authentication** | Anyone who can reach the URL | Paid call per request | **Nothing** | No auth or rate limit |
| 23 | Prompt injection | Ticket text gives the model instructions | Model may follow them | **Nothing** | Customer text and instructions share one context |
| 24 | Huge input | 1 MB description | Anthropic: 400 → 502. **Ollama: prompt cut from the front** | Anthropic: **502** + log. Ollama: **nothing** (only Ollama's own server log) | No length limit |
| 25 | Diagnostics exposed | Response always includes internals | Leaks model, tool calls, cost | **Code review** only | Not switched off in prod |
| 26 | Wrong environment | `Development` set in prod | Swagger and `/dev/eval-tickets` are public | **Nothing** at runtime | No startup check |

## 6. Ops, config and logging

| # | Failure | Trigger | What happens today | Detected by | Gap |
|---|---|---|---|---|---|
| 27 | API key missing in prod | User-secrets only in Development | Startup crash | **Startup crash** + exception on the console | No prod secret source |
| 28 | Personal data in logs | Raw replies and prompts logged | Customer text on disk | **Nothing** (it's in the logs themselves) | No redaction or retention |
| 29 | Logs lost or disk full | Local log files | Lost on restart, split across instances | **Nothing** until you need the logs | No central logging |
| 30 | No health check or metrics | — | — | — (this row is the detection gap itself) | No `/health`, no metrics |

## Count

- **Nothing:** 15 rows (6, 9, 12, 14, 16, 20, 21, 22, 23, 26, 28, 29, plus the Ollama half of 24, single failures in 18, and totals in 11).
- **Status code + log:** 10 rows. These are the failures you planned for.
- **Diagnostics only, nobody watching:** 3 rows (11, 13, 19).

So the code handles errors well, but bad answers (rows 12–16) and abuse (rows 22–23) currently go unnoticed.
Those rows are the case for adding output checks and metrics before going to production.

## Where to start

- **Most severe:** #22 (no auth, so anyone can spend the budget), #12 (wrong answers returned as 200) and #23 (prompt injection).
- **Most likely to surprise:** #24 on Ollama (the context window is cut off without any error), #19 (fine with the fake data, breaks with real data) and #10 (a cut-off reply looks like a parse error).
