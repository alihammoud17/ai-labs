# ai-lab

A small .NET console chat app I built to learn how LLM integration works in practice: streaming, conversation history, token costs, and the settings that change a model's behaviour.

It isn't a product. It's a lab bench. You chat with a model in the terminal, change one setting, run it again, and write down what changed. The results are in [`experiments.md`](experiments.md).

## What it does

- **Chats in the terminal** with a local model (Llama 3.2 through Ollama, free) or with Claude Haiku 5.5 through the Anthropic API.
- **Streams replies** as they're generated, instead of waiting for the full answer.
- **Remembers the conversation.** The whole history is sent with every message, which is how the model "remembers" anything.
- **Shows what each turn cost**, with input and output tokens, the finish reason, the turn's cost, and a running total for the session:

  ```
  Tokens: in=123, out=93 | Finish Reason: stop | Turn: $0.000059 | Session: $0.000108
  ```

- **Warns when an answer was cut off.** If the model hits the output limit, you see `⚠ Answer was cut off`. Without that warning, a truncated answer looks like a normal one.
- **Can force JSON replies.** A system message asks for `{"answer": ..., "confidence": ...}`, and every reply is checked with a strict JSON parse (`✅ valid JSON` / `❌ invalid JSON`).

All the app code talks to `IChatClient` from `Microsoft.Extensions.AI`, so switching between Ollama and Claude is a one-line config change. Nothing else in the code has to know which provider it's using.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) with the Llama 3.2 model, for free local use:

  ```bash
  ollama pull llama3.2
  ```

- An Anthropic API key, only if you want to use Claude.

## Getting started

```bash
git clone https://github.com/alihammoud17/ai-labs.git
cd ai-labs
dotnet run --project AiLab.Console
```

By default it uses Ollama, so make sure Ollama is running first.

### Using Claude instead

1. Store your API key with user-secrets. It never goes in code or config files.

   ```bash
   cd AiLab.Console
   dotnet user-secrets set "Anthropic:ApiKey" "your-key-here"
   ```

2. Switch the provider in `AiLab.Console/appsettings.json`:

   ```json
   {
     "Chat": {
       "Provider": "anthropic",
       "Model": "claude-haiku-5-5"
     }
   }
   ```

Switch it back to `ollama` / `llama3.2` when you're done, so day-to-day testing stays free.

## Using the app

| Input | What it does |
|---|---|
| any text | Sends a message |
| `/reset` | Starts a fresh conversation (keeps the system message) |
| `/exit` | Quits |
| Ctrl+C while a reply is streaming | Stops that reply only, and discards the turn |
| Ctrl+C at the prompt | Quits |

### Settings worth knowing

These live at the top of `Program.cs` and are meant to be edited between experiments:

- **`options`** (`ChatOptions`) sets temperature, max output tokens and similar. Empty means "use the provider's defaults".
- **`jsonSystemMessage`** is the format-forcing system message. It's **on by default**; set it to `null` for normal chat.
- **`MaxHistoryMessages`** (20) is a simple cap on history length. The oldest exchange is dropped first, and system messages are never trimmed.

## Project structure

```
AiLab.Console/
  Program.cs                      the chat loop: streaming, history, costs, commands
  Providers/ChatClientFactory.cs  creates the Ollama or Anthropic IChatClient
  Pricing/ModelPrices.cs          price table and the per-turn cost calculation
  appsettings.json                which provider and model to use
experiments.md                    what I tested, what I expected, what actually happened
docs/plans/                       the plan for each step, with what changed along the way
```

## What I learned

The details are in [`experiments.md`](experiments.md). The short version:

- **History is the hidden cost.** Every message re-sends the whole conversation, so input tokens climb every turn: from 18 to 339 in five short turns.
- **Thinking models spend tokens you don't see.** Claude Haiku 5.5 thinks before answering. With a 10-token limit, it used all of it on thinking and returned nothing at all.
- **Settings aren't portable.** Haiku only accepts its default temperature, and with no output limit set it still stopped at 1,024 tokens. The same `ChatOptions` behaved differently on each provider.
- **Valid JSON isn't a correct answer.** Llama returned perfectly formatted JSON that said the longest river is the Niger, with 90% "confidence".

The whole experiment stage cost about $0.0015 in API calls. Everything else ran locally for free.

## Status

This is the first stage of a self-study plan on AI integration in .NET. All four steps are done: chat loop, streaming, cost tracking, and ChatOptions experiments.
