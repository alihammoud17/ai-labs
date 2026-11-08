# Choosing the chunker and the model with data: the experiment matrix (5–7 Nov 2026)

A page for someone new to this repo: what was decided on Thursday of week 5, how, and what it showed. [baseline-2026-11-03.md](baseline-2026-11-03.md) explains the project and the 30-question test set, and [hybrid-search-2026-11-04.md](hybrid-search-2026-11-04.md) explains the retrieval metrics.

## The question

Two settings of `POST /ask` were there by habit:

- **Which chunks?** `heading` (one chunk per markdown section, 50 chunks, ~176 tokens each) or `fixed` (2,000-character windows, 21 chunks, ~440 tokens each).
- **Which model writes the answer?** Claude Haiku 5.5 (paid, over the network) or llama3.2 3B (free, on the laptop through Ollama).

Two choices of two make four configurations, or "cells": **HH** (heading + Haiku), **HL** (heading + llama), **FH** (fixed + Haiku), **FL** (fixed + llama).

## How it was measured

Each cell answered the 30 questions 3 times through the real `/ask`, with everything else held the same (top 5, vector search, the same prompt, Haiku as the judge). Fresh model calls for every cell, because a cached answer comes back in 0 ms and would ruin the latency numbers.

- **Quality:** pass/fail by hand on 12 answerable questions (run 1 of each cell), against the gold answers. The judge's Groundedness score is shown too, but only as a signal: it hasn't been reliable enough to decide on its own.
- **Refusing:** the 4 questions the docs don't cover, 3 runs each. Does the answer say "the docs don't cover this"?
- **Cost per answer:** `/ask`'s own tokens × the model's price (judge calls excluded).
- **Latency:** retrieval + generation time, median and 95th percentile (p95: 1 answer in 20 is slower than this).

**The decision rule was written before the runs:** a cell is out below 10/12 correct, below 11/12 correct refusals, if the off-topic question E5 isn't stopped 3/3, or above 5 s p95. Among the rest, the most correct wins; cost breaks ties. Writing it first stops you from picking the rule that favours the cell you already like.

## The result

| Cell | Correct /12 | Refusals /12 | Cost per 1,000 answers | Latency median / p95 |
|---|---|---|---|---|
| **HH** heading + Haiku | **12** | **12** | $0.44 | 1.38 s / 3.30 s |
| FH fixed + Haiku | 9 | 12 | $0.68 | 1.49 s / 4.75 s |
| HL heading + llama | 6 | 6 | $0 | 1.17 s / 2.72 s |
| FL fixed + llama | 1 | 7 | $0 | 1.34 s / 3.34 s |

**Chosen: heading + Haiku**, the only cell that clears every bar. `Ask:Chunker` stays `heading`. `AiLab.Rag/appsettings.json` now uses Haiku, and `appsettings.Development.json` keeps llama3.2, so local development and the tests stay free.

All four cells together cost about $0.55.

## What it showed

- **The model mattered more than the chunker.** Switching to llama lost 6 questions (heading) and 8 (fixed). Switching to fixed lost 3 (Haiku) and 5 (llama).
- **Bigger chunks didn't help lists or two-source questions; they hurt them.** With top 5 fixed, fewer distinct places in the docs make it into the context. Two facts (B6's MFA limit, G1's second contract) never reached the model.
- **A character window can cut a line in two.** All five rate limits sit on one long line. The fixed chunker split it, and B7 got only the second half. The recall metric still counted a hit, because it checks lines, not whole facts.
- **llama answers from general knowledge when the docs are silent.** "What is multi-tenancy?" got a textbook definition in 6 of 6 llama runs, and Haiku said "not in the docs" in 6 of 6.
- **The judge misses omissions.** Seven answers that left out a required fact still scored 4 or 5 for Groundedness, which checks that what's said is supported, not that it's complete. Wrong citations get past it too.
- **Haiku's JSON repair was broken, and these runs found it.** Sending Haiku's reply back with its signed thinking block was refused (a 502). The repair now sends only the reply's text, with a test.

Full numbers: `experiments.md` section 17. Per-answer scores: `eval/results/2026-11-05-matrix-hand-scores.md`. Plan: `docs/plans/2026-11-05-week-5-experiment-matrix.md`.
