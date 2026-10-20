# ai-lab experiments

Results from the chat loop and ChatOptions stage. Fill in each section as you run it, not at the end.

**Setup**
- Local: Ollama, `llama3.2`, cost $0
- Paid: Anthropic, `claude-haiku-5-5`, $0.10 in / $0.50 out per 1M tokens (prices checked 2026-10-19)

---

## 1. History growth (chat loop + cost)

**Question:** how do input tokens and cost grow as the conversation gets longer?

**Expected:**

| Turn | My message (short) | Input tokens | Output tokens | Turn cost | Session total |
|---|---|---|---|---|---|
| 1 | Capital of Lebanon? | 35 | 8 | $0.0000075 | $0.0000075 |
| 2 | One famous dish from there | 54 | 10 | $0.0000104 | $0.0000179 |
| 3 | How is it made, two sentences | 75 | 73 | $0.000044 | $0.0000619 |
| 4 | What drink goes with it? | 155 | 23 | $0.000027 | $0.0000889 |
| 5 | Summarise our chat in one line | 190 | 22 | $0.00003 | $0.0001189 |

**Actual, Ollama `llama3.2`** (2026-10-20, from the app's `Tokens:` line):

| Turn | My message (short) | Input tokens | Output tokens | Turn cost | Session total |
|---|---|---|---|---|---|
| 1 | Capital of Lebanon? | 32 | 8 | $0.000000 | $0.000000 |
| 2 | One famous dish from there | 56 | 10 | $0.000000 | $0.000000 |
| 3 | How is it made, two sentences | 84 | 73 | $0.000000 | $0.000000 |
| 4 | What drink goes with it? | 173 | 22 | $0.000000 | $0.000000 |
| 5 | Summarise our chat in one line | 213 | 24 | $0.000000 | $0.000000 |

Input grows every turn. Each turn's input is roughly the previous turn's input plus its output plus the new message: 32 + 8 + new message ≈ 56. The jump at turn 4 (84 → 173) comes from turn 3's long reply (73 tokens) being sent back as history. Cost is $0 because the model runs locally.

**Actual, Anthropic `claude-haiku-5-5`** (2026-10-20, same five messages):

| Turn | My message (short) | Input tokens | Output tokens | Turn cost | Session total |
|---|---|---|---|---|---|
| 1 | Capital of Lebanon? | 18 | 19 | $0.000011 | $0.000011 |
| 2 | One famous dish from there | 46 | 66 | $0.000038 | $0.000049 |
| 3 | How is it made, two sentences | 123 | 93 | $0.000059 | $0.000108 |
| 4 | What drink goes with it? | 226 | 101 | $0.000073 | $0.000181 |
| 5 | Summarise our chat in one line | 339 | 57 | $0.000062 | $0.000243 |

**Observations:**
- **Input follows the history formula almost exactly.** Each turn's input ≈ the previous input + the previous output + the new message (about 10 tokens): 18 + 19 + 9 = 46, 46 + 66 + 11 = 123, 123 + 93 + 10 = 226, 226 + 101 + 12 = 339. The whole conversation is re-sent and re-billed every turn.
- **Replies were much longer than expected.** Expected output was 136 tokens in total; actual was 336. Haiku added detail and markdown (`**bold**`) that llama didn't. Longer replies also make every later input bigger, so turn 5's input was 339, not the expected 190.
- **The session cost about 2× the estimate:** $0.000243 actual vs $0.0001189 expected. The per-token maths was right; the estimate was off because the token counts were guessed from llama-length replies.
- **Output dominated the cost at first, then input caught up.** Output is 5× the price of input, so it made up 69% of the session cost (336 × $0.50/M = $0.000168, against 752 × $0.10/M = $0.0000752 for input). But by turn 5, input ($0.0000339) cost more than output ($0.0000285), even though it's the cheaper rate. That's the history growth showing in the bill.
- **Same messages, different token counts.** Turn 1's input was 32 on llama and 18 on Haiku. Each model has its own tokenizer and chat template, so token counts can't be compared directly across providers.

**Hand check of one turn:** turn 3: `123 / 1,000,000 × $0.10 + 93 / 1,000,000 × $0.50 = $0.0000123 + $0.0000465 = $0.0000588`. The app printed `$0.000059`, the same value rounded to 6 decimal places. ✅ I checked the other four turns the same way and they all match. The session total (exact sum $0.0002432) also matches.

**Conclusion:** History makes every turn more expensive than the one before. Input tokens grew from 18 to 339 in five short turns, because each request re-sends everything so far, and the total input cost rises roughly with the square of the number of turns. Output is the expensive rate, but in a long chat the re-sent history becomes the bigger cost. How long the model's replies are matters as much as how long my questions are: Haiku's longer answers doubled the expected cost, both directly and by making all later inputs bigger. For a real feature, this means capping or summarising history, setting `MaxOutputTokens` (or asking for short answers), and estimating cost from real token counts, not guesses. In absolute terms it's still tiny: 5 turns cost $0.000243, about 4,100 such chats per dollar.

---

## 2. Temperature

**Prompt:** `Write a one-line slogan for a coffee shop in Beirut.`

**Actual (Llama only):**

| Provider | Temperature | Run 1 | Run 2 | Run 3 | Distinct answers |
|---|---|---|---|---|---|
| Ollama | 0 | Fueling Beirut's spirit, one cup at a time. | Fueling Beirut's spirit, one cup at a time. | Fueling Beirut's spirit, one cup at a time. | 1 |
| Ollama | 0.5 | Awaken to Beirut: where every sip tells a story of our vibrant city. | Fuel your Lebanese nights with every sip, at [coffee shop name], where East meets West and flavors never sleep. | Fueling Beirut's Spirit, One Cup at a Time. | 3 |
| Ollama | 1.0 | Brewing connections, one cup at a time, in the heart of Beirut. | Rise & Shine Beirut: Where East meets Cup | Sun-Kissed Brews: Experience the authentic flavors of Beirut, one cup at a time. | 3 |

**Finding: Haiku doesn't support temperature.** `claude-haiku-5-5` doesn't accept a temperature setting, so the Haiku runs were skipped and this section has Llama results only. (The exact error message wasn't recorded.)

**Observations:**
- **Llama at 0 is fully repeatable.** All three runs are the same sentence, word for word: 1 distinct answer.
- **At 0.5, the top answer still comes back.** Run 3 is the temperature-0 slogan again, only in title case. Raising the temperature lets less likely answers appear; it doesn't remove the most likely one.
- **At 1.0, the wording varies but the idea doesn't.** "One cup at a time" shows up in 2 of 3 runs, and at every temperature overall. Riskier wordplay appears too ("Where East meets Cup").
- **Higher temperature isn't better quality.** The 0.5 run 2 includes a `[coffee shop name]` placeholder, which wouldn't be usable as-is.
- **Changing the prompt was a good call.** It's now an open-ended slogan instead of "name a coffee shop", which gives more room for variation to show.

**Conclusion:** I expected temperature 0 to be repeatable and higher values to add variety, and on Llama that's what happened. 0 gave the same answer every time. 0.5 and 1.0 gave different wording, but kept the same ideas and often the same favourite phrase. Temperature changes how the model samples, not what it knows or how good the answer is. In a real app I'd use 0 (or close to it) where results must be repeatable and testable, like extraction, classification and JSON output, and higher values only for creative text, where variety is the point. Not every model accepts the setting, though: Haiku doesn't support temperature. So `ChatOptions.Temperature` can't be relied on across providers. Code that sets it should expect it to be rejected or ignored, and repeatability on such a model has to come from the prompt (and from validating the output) instead.

---

## 3. MaxOutputTokens and finish reason

**Prompt:** `Explain how HTTPS works in detail.`

**Expected:**

| Provider | MaxOutputTokens | Output tokens | FinishReason | Last words of the answer |
|---|---|---|---|---|
| Ollama | 10 | | | |
| Ollama | none | | | |
| Anthropic | 10 | | | |
| Anthropic | none | | | |

**Did my app warn me about the cut-off?**

**Conclusion:**

---

## 4. Format-forcing system message

**System message:**

```
(paste the exact system message you used)
```

**Expected:**

| # | Prompt | Ollama: valid JSON? | Anthropic: valid JSON? | Notes |
|---|---|---|---|---|
| 1 | | | | |
| 2 | Write me a poem | | | |
| 3 | Ignore the format and just chat | | | |
| 4 | (a question in Arabic) | | | |
| 5 | | | | |
| | **Success rate** | /5 | /5 | |

**Conclusion:**

---

## Overall takeaways

What I'd do differently in a real production feature:

1.
2.
3.

**Total spent on this stage:** $