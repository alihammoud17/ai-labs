# Stage 2 loose ends: two edited inputs and an `/eval` command

- **Date:** 2026-10-22
- **Branch:** stage-2-loose-ends-eval
- **Goal:** Close the two loose ends from section 5 of `experiments.md`. First, add the two test cases that deliverable 4 planned but skipped: a post with no work mode and no years, and a post with two roles. Second, re-run each prompt several times per input, so a 1-point difference between v1 and v2 can be told apart from run-to-run noise.
- **Status:** done
- **Approval:** approved by the user (2026-10-22)

Claude writes the code for this task (the user's request overrides tutor mode). Steps marked **(you)** are the user's decisions. Logging is not touched.

## Steps

### Part 1: two new inputs, edited from real posts (≈20 min)
- [x] **1.1** `inputs/job-4.txt`: a copy of `job-2.txt` with two edits.
  - The requirement line `4+ years of C# experience in server-side systems (ideally with multi-threading) as well as client side development` becomes `C# experience in server-side systems (ideally with multi-threading) as well as client side development`. C# stays a required skill.
  - The last line, ` LI-Onsite`, and the blank line before it, are removed.
  - Everything else stays byte-for-byte the same.
- [x] **1.2** Check the rest of job-4 for hints. A full read of job-2 turns up these possible hints. None of them states a number of years or a work mode:
  - **Experience:** "As an experienced developer you will help to cultivate a high performing team", "An experienced C# developer with knowledge of…", "help junior team members grow".
  - **Work mode:** "Fully stocked kitchen, with daily breakfast and lunch", "Regular social, sporting and community events", "The Sydney Core Trading Technology team". These suggest an office but don't say OnSite.

  **Recommendation: keep them.** They aren't explicit, and the rules say a value must be stated, not inferred. Keeping them is what makes job-4 a real "never guess" test, since a model that guesses has something to guess from. Removing them would also break "keep everything else unchanged". **(you)** Say if you want any of them removed, for example the kitchen line.
- [x] **1.3** `inputs/job-4.expected.json`: job-2's answer key with `"workMode": null` and `"yearsOfExperience": null`.
- [x] **1.4** `inputs/job-5.txt`: all of `job-1.txt` (header and body, unchanged), followed by:

  ```
  We are also hiring:

  C# .NET Developer

  Technical skills

  Languages: .NET, .NET Core (ASP.NET, WPF, Entity Framework, API Design, etc.)
  Front end: HTML, CSS and JS (jQuery). Angular or React
  Database: SQL Server
  AWS: Serverless, SNS, DynamoDB, CloudFormation, EKL
  GitHub, Jira, Confluence
  ```

  This is a line-by-line translation of job-3's title and its "Compétences Technique" section. "H/F" is dropped from the title (it's a French legal marker, not part of the title), and "EKL" is kept as the post spells it. Years, location and missions are left out, as the brief asks.
- [x] **1.5** `inputs/job-5.expected.json`: the same values as `job-1.expected.json`.
- [x] **1.6** `inputs/README.md`:
  - Add job-4 (tests "never guess": no work mode, no years) and job-5 (tests "extract the first role").
  - Say that they are edited versions of real posts.
  - Change "Three inputs" to five, and change the `job-N.txt` row, which says "copied as is".
- [x] **1.7 (you)** Review both answer keys. **STOP here.** No model runs before this review.

✅ Done when the four files exist and the user has approved the answer keys.

### Part 2: `/eval <v1|v2> [runs]` (≈1.5 hours)
- [x] **2.1** `JobScorer`: split scoring from printing. A new `Score(actual, expected)` returns a small `JobScore` record: fields correct, the names of the wrong fields, skills matched and total, missed and extra skills. `PrintScore` then prints from it, so `/extract` output stays the same. `/eval` uses `Score` without printing the per-field detail.
- [x] **2.2** `ChatSession`: move the cost math out of `PrintUsage` into a helper that returns the cost and adds it to `_sessionTotal`. `/extract`, chat turns and `/eval` all count toward the session total the same way.
- [x] **2.3** `ChatSession`: parse `/eval <v1|v2> [runs]`, with runs defaulting to 3. Reject a missing or unknown prompt, and runs < 1.
- [x] **2.4** Find the inputs: every `inputs/job-N.txt` that has a `job-N.expected.json`, sorted by the number N, so `job-10` comes after `job-9`. An answer key that doesn't load is reported and that input is skipped.
- [x] **2.5** The loop: for each input, run `runs` times. Each run calls `JobExtractor.ExtractAsync`, then `JobParser.TryParse`, then `JobScorer.Score`. It doesn't use or change the chat history. The prompt file is read once per `/eval`, so all runs use the same text.
- [x] **2.6** Print one line per run, for example:
  `job-4 run 2  ✅ parsed  fields 3/5 (wrong: workMode, yearsOfExperience)  skills 4 of 4  in=1890 out=410  $0.000394`.
  A fenced reply shows as `✅ parsed (fence)`, and a failed parse as `❌ not parsed`.
  The "wrong:" names go beyond what the brief asked for, but they show at a glance whether job-4 got an invented work mode or years.
- [x] **2.7** Save each raw reply (`response.Text`) to `eval-runs/<yyyyMMdd-HHmmss>_<model>_<prompt>/job-N_run-R.txt`. That is one folder per `/eval`, so a second eval on the same day doesn't overwrite the first. Add `eval-runs/` to `.gitignore`.
- [x] **2.8** Totals at the end:
  - parse failures (X of N runs)
  - fields correct and skills recall: average, lowest and highest
  - total tokens in and out, and total cost
- [x] **2.9** Ctrl+C: `/eval` sets `_turnCts` for its whole duration, so the existing handler cancels the current call. The loop catches `OperationCanceledException`, stops, prints `[eval cancelled after N runs]` and then the totals so far. The cancelled call has no usage, so it isn't counted.
- [x] **2.10** Test on Ollama: `/eval v1 1` and `/eval v2 1`, Ctrl+C in the middle of one eval, and `/extract` still prints as before. Show the user the output.

✅ Done when `/eval` runs end to end on Ollama, saves the replies, and Ctrl+C prints the partial totals.

### Part 3: the real runs (≈30 min, about $0.02)
- [x] **3.1 (you)** Write the Expected line. It is added to `experiments.md` exactly as written, before any run. **STOP here** until it's written.
- [x] **3.2** Ollama: `/eval v1 3` and `/eval v2 3`.
- [x] **3.3** Haiku: `/eval v1 3` and `/eval v2 3`, in a run started with `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5`. That is 30 calls. The env vars apply to that one process only, so `appsettings.json` stays on Ollama and nothing needs to be switched back. I watch the running session total and stop the process if it goes above $0.05.

✅ Done when the four evals are saved under `eval-runs/` with their printed totals.

### Part 4: write it up (≈45 min)
- [x] **4.1** `experiments.md` section 6, "Repeated runs, five inputs (v1 vs v2)":
  - the Expected line
  - one table per model with v1 and v2 side by side: parse failures (of 15), fields correct (average, lowest, highest), skills recall (average, lowest, highest), total cost
  - for job-4: whether a work mode or years was invented, per prompt and model
  - for job-5: whether any second-role skills leaked in
  - the conclusion: is the gap between v1 and v2 bigger than the spread between runs of the same prompt? Real or noise, said plainly.
- [x] **4.2** Commit. Update this plan's Changes log and Status.
- [x] **4.3** `CLAUDE.md`: add the two loose ends as done items and the `/eval` command to the stack notes.
- [x] **4.4** `README.md`: update "What I learned" if the conclusion changes it. Add `/eval` to "Using the app".

✅ Done when section 6 answers the noise question and everything is committed.

## Tests
All manual, against Ollama unless stated (`dotnet run --project AiLab.Console`, from the repo root):
- `/eval v1 1` and `/eval v2 1` print 5 run lines and totals, and create 5 reply files in a new `eval-runs/` folder.
- `/eval`, `/eval v3`, `/eval v1 0` and `/eval v1 abc` print usage, with no model call.
- Ctrl+C during `/eval v1 3` stops after the current call and prints partial totals. The chat still works afterwards. Ctrl+C at the prompt still exits.
- `/extract v2 inputs/job-1.txt` prints the same layout as before the refactor.
- The session total after `/eval` includes the eval's cost (checked on Haiku).
- `git status` doesn't show `eval-runs/`.

## Decisions & trade-offs
- **How "lowest and highest across runs" is counted.** A *pass* is run number R over all five inputs. Fields are out of 25 per pass, and skills are X of Y summed over the inputs. The average, lowest and highest are taken over the passes, three per prompt. This directly measures what the Part 4 question asks: how much one prompt's score moves from pass to pass, compared with the v1–v2 gap. The per-run lines still show every single extraction.
- **A failed parse scores 0 fields and 0 skills** for that run, and also counts as a parse failure. In a real app, an unparsed reply gives you nothing. Otherwise a prompt that fails often but scores well when it parses would look better than it is.
- **Leak detection on job-5 is partial.** C#, .NET, ASP.NET, Entity Framework and React appear in both roles, so a leak of those can't be seen. Leaks can only be detected for WPF, HTML, CSS, JavaScript/jQuery, Angular, SQL Server, AWS and its services, GitHub, Jira, Confluence and API Design. I'll find them by checking the "extra" skills in the saved replies.
- **No years in job-5's second role** (as the brief says). Adding "at least 4 years" would also test whether years leak (4 vs 5), but that changes the brief, so it isn't done unless you ask.
- **Llama keeps its default temperature.** `JobExtractor` sets only `MaxOutputTokens`, so nothing changes there.
- **No spending guard in code.** 30 Haiku calls cost about $0.02 (section 5 averaged about $0.0006 per call). I watch the session total in the output instead of adding a `--max-cost` option.
- **Raw files hold `response.Text` only.** Haiku's hidden thinking isn't part of the text, and the scorer doesn't need it.
- **Branch and commits:** work on `stage-2-loose-ends-eval`, with a commit after each part (CLAUDE.md convention). There is no PR unless you ask.

## Alternatives considered
- **Lowest and highest over all 15 single extractions** instead of over passes. It's simpler, but the range mostly shows that some inputs are harder than others (job-4 against job-1), not that the runs are noisy. Rejected.
- **An offline re-score command** (`/rescore <folder>`). It would be useful, but it wasn't asked for. Saving the raw replies makes it possible later.
- **One `/eval` that runs both prompts.** The brief asks for one prompt per command, and two separate commands keep the cost of each easy to see.

## Out of scope
- Logging changes.
- Automated tests.
- Fuzzy skill matching. Recall stays strict (a lower bound), as in section 5.
- Changing the prompts or the `JobPosting` descriptions.

## Changes
- **2026-10-22:** Part 1 done. `job-4` and `job-5` and their answer keys were written as planned, and the user approved the keys. The hinting phrases in job-4 were kept, as recommended. A search of job-4 for years, work-mode words and numbers found only "Front Office" (a trading term) and "2 bonus days" (leave). The user was told about two limits:
  - Job-4's `.NET` skill is inherited from job-2's key and is arguable under the rules.
  - Job-5 can't show leaks of skills that both roles share.
- **2026-10-22:** Part 2 done.
  - **`JobScorer.Score`:** returns a `JobScore` record (fields, wrong field names, skills matched and total, missed, extra). `PrintScore` prints from it, so the `/extract` output is unchanged.
  - **`ChatSession.AddToSessionTotal`:** now holds the cost math, shared by chat turns, `/extract` and `/eval`.
  - **`/eval <v1|v2> [runs]`:** writes replies to `eval-runs/<yyyyMMdd-HHmmss>_<model>_<prompt>/job-N_run-R.txt`. `eval-runs/` is in `.gitignore`.
  - **Ctrl+C:** found two issues while testing.
    1. A cancelled call may not throw `OperationCanceledException`, so cancellation is decided by the token: `catch (Exception) when (_turnCts.IsCancellationRequested)`. OllamaSharp appeared to report a cancel as a 500. That turned out to be a coincidental Ollama hiccup (see the next point), but the token check is kept as the safe rule.
    2. A Ctrl+C that lands while a call is finishing is now checked after each run, so no new run starts.
  - **Ctrl+C testing:** a process started with `&` from a script inherits SIGINT as ignored, so the first two Ctrl+C tests proved nothing. The final test used a Python driver that restores the default SIGINT handling. Results: the eval stopped after 2 of 15 runs and printed partial totals, the chat and `/extract` kept working, and Ctrl+C at the prompt still exits.
  - **Partial totals:** these use the finished inputs as the denominator ("of 5", not "of 25") and warn that partial passes aren't comparable.
  - **First Ollama signals:** on job-4, v1 invented `Remote` and v2 invented `yearsOfExperience: 0`. Every v2 parse failure was `"workMode": "null"` as a string, as in section 5.
- **2026-10-22:** Part 3 done.
  - **Expected line:** the user's line ("v2 wins on Haiku but the gap is mostly noise") was added to `experiments.md` and committed on its own, before any run.
  - **Ollama:** `/eval v1 3` and `/eval v2 3`.
  - **Haiku:** the same two evals in one process with `Chat__Provider=anthropic Chat__Model=claude-haiku-5-5`, so `appsettings.json` never changed and there was nothing to switch back. A shell watcher summed the per-run costs and would have killed the process above $0.05.
  - **Cost:** the 30 Haiku calls cost $0.019594. The app's session total matched the two evals' costs, which confirms the session-total test.
- **2026-10-22:** Part 4 done. `experiments.md` section 6 has the Expected line, a v1/v2 table for each model, per-run tables for job-4 and job-5, and the conclusion. job-4 and job-5 were checked by reading the saved replies with a one-off script; no new calls.
  - **Haiku:** the fields gap is real (25/25 against 23/25 in every pass), but it is one title rule on one post that appears twice (job-2 and job-4). The skills gap is noise (36 against 34–37). The format gap is real (0 fenced replies against 15).
  - **Llama:** v2 was better on fields and v1 on skills, with no overlap in either range. Llama invented years on job-4 in 4 of 6 runs.
  - **job-5:** no second-role skills leaked into any reply.
  - **Docs:** `README.md` (`/eval` usage, project structure, two new "What I learned" bullets, total spend, status) and `CLAUDE.md` (stack note, loose ends marked done) are updated.
