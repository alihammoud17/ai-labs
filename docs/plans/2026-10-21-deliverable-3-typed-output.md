# Deliverable 3: typed output (job postings)

- **Date:** 2026-10-21
- **Branch:** stage-2-di-logging-structured-output
- **Goal:** Turn a real job posting into a typed C# record with `GetResponseAsync<JobPosting>`. Score the result against a hand-written golden answer, on Llama and on Haiku.
- **Status:** done
- **Approval:** approved by the user in chat (2026-10-21)
- **Parent plan:** `docs/plans/2026-10-21-stage-2-di-logging-structured-output.md`, Deliverable 3. On request, Claude writes the code this time, which overrides tutor mode for this deliverable. Logging is not touched.

## Steps

### 1. Test input files
- [x] Create `inputs/job-1.txt` with exactly the provided posting text: the three-line LinkedIn header, then the post body.
- [x] Create `inputs/job-1.expected.json` with the provided golden answer, unchanged.
- [x] Create `inputs/README.md`. It explains the naming (`job-N.txt` is a real posting, `job-N.expected.json` is its golden answer) and lists the extraction rules:
  - **Title:** without the sub-brand suffix, so " - Acom" is dropped.
  - **Company:** the hiring company, not a sub-brand.
  - **Location:** "City, Country" when the post states it.
  - **Work mode:** OnSite, Hybrid, Remote, or null. Never guess.
  - **Required skills:** from the Requirements section only, at CV-keyword level, about 12 at most.
  - **Years of experience:** the minimum stated, even after "ideally"; otherwise null.
  - **Skills scoring:** recall.

  It also lists the planned inputs:
  1. long and messy (done)
  2. clean
  3. French
  4. missing details
  5. tricky

### 2. The typed record
- [x] Create `Extraction/JobPosting.cs` with:
  - `record JobPosting(string Title, string? Company, string? Location, WorkMode? WorkMode, List<string> RequiredSkills, int? YearsOfExperience)`
  - `enum WorkMode { OnSite, Hybrid, Remote }`
- [x] Add `[Description]` attributes to the record and to each property, stating the rules from `inputs/README.md`. `GetResponseAsync<T>` puts them into the JSON schema the model sees.
- [x] Put `[JsonConverter(typeof(JsonStringEnumConverter<WorkMode>))]` on the enum. The value is then a string (`"Hybrid"`) in the schema sent to the model, when parsing the reply, and when reading the golden file.

### 3. The `/extract` command
- [x] Create `Extraction/JobExtractor.cs`. It takes `IChatClient` from DI and calls `GetResponseAsync<JobPosting>` with the v1 instruction "Extract the job details from this posting." plus the file text. It builds its own message list, so the chat history is never touched. Register it in DI and inject it into `ChatSession`.
- [x] In `ChatSession`, `/extract <path>` reads the file and calls the extractor. Through `TryGetResult` it prints each field on its own line, or a clear "could not parse" message with the raw reply text. It then prints tokens, cost and finish reason with the existing `PrintUsage`, so the cost also counts toward the session total.
- [x] Create `Extraction/JobScorer.cs`. If `<name>.expected.json` exists next to the input, load it and print the score:
  - **Exact match per field** for title, company, location, workMode and years.
  - **Skills recall** as "matched X of Y", with the misses listed.

### 4. Run and verify
- [x] Run `/extract inputs/job-1.txt` on Ollama (llama3.2) and show the output.
- [x] Run it once on Anthropic (claude-haiku-5-5).
  - If the provider rejects the structured-output request, retry with `useJsonSchemaResponseFormat: false` and report the exact error and the change.
- [x] Switch the provider back to Ollama.

### 5. Record results
- [x] Add "Deliverable 3: typed output (job-1)" to `experiments.md`. For both providers, record:
  - whether it parsed
  - the field-by-field score and skills recall
  - tokens and cost
  - anything surprising: invented values, wrong work mode, nice-to-have skills included
- [x] Commit, update this plan's Changes log, tick D3 in the stage plan, and update `CLAUDE.md`'s current step.

## Tests
All manual (E2E, `dotnet run --project AiLab.Console`, started from the repo root):
- **Build:** `dotnet build` succeeds with no warnings.
- **Llama:** `/extract inputs/job-1.txt` prints the six fields, the score lines, and the tokens/cost/finish-reason line. A chat turn after it shows input tokens unchanged by the extraction, which proves the history wasn't touched.
- **Failure paths (Llama, free):**
  - `/extract` with a missing file prints an error and the loop continues.
  - `/extract` with no path prints usage.
  - An input with no `.expected.json` prints the fields without a score.
- **Haiku:** one run of `/extract inputs/job-1.txt`. Parsed or not, the score and the cost are recorded.

## Decisions & trade-offs
- **JSON names:** `GetResponseAsync<T>` uses `AIJsonUtilities.DefaultOptions`, which are web defaults: camelCase and case-insensitive. The golden file is read with the same options, so `title` / `workMode` / `requiredSkills` match without `[JsonPropertyName]` on every property.
- **Enum as strings:** set on the enum type itself, so the schema, the model's reply and the golden file all agree with no extra options.
- **Separate classes:** `JobExtractor` (the model call) and `JobScorer` (the comparison) keep `ChatSession` as just the command router. `ChatSession` still only knows `IChatClient`, and there's no provider code.
- **Cost:** printed with the existing `PrintUsage` and added to the session total, because the money is spent either way.
- **Output limit on the extraction call:** set `MaxOutputTokens = 2000`. Section 3 showed Haiku thinks by default and stopped at 1024 tokens when no limit was set. Thinking plus a 12-skill JSON object could get cut off, and a truncated JSON can't be parsed, which would waste the single paid run. The finish reason is printed, so a cut-off still shows.
- **Paths:** `/extract <path>` resolves relative paths from the current directory. Running from the repo root makes `inputs/job-1.txt` work. `inputs/` isn't copied to the output folder, because these are data you edit.
- **Scoring:**
  - Field matches are exact after trimming, case-insensitive.
  - Skills recall uses exact, case-insensitive names. Misses are listed so a human can accept equivalent wording ("ASP.NET Core" vs "ASP.NET"), as `inputs/README.md` describes. The automatic number is a lower bound.
  - Extra skills the model adds are listed too, but they don't lower recall.
- **Native JSON schema first:** each provider is tried with the default (`useJsonSchemaResponseFormat` not set). The fallback is only used if a provider rejects the request.

## Alternatives considered
- `[JsonPropertyName]` on every property: rejected as redundant with the web defaults.
- Putting the extraction logic directly in `ChatSession`: rejected because the loop is already long, and deliverable 4 reuses the extractor with other prompts.
- Leaving `MaxOutputTokens` at the provider default: rejected for the paid run, for the reason above.

## Out of scope
- Prompt v2, `prompts/*.txt` files and inputs 2–5. These are for deliverable 4.
- Automatic "same meaning" skill matching. Equivalent wording is judged by a human.
- Any change to logging.

## Changes
- **2026-10-21:** Implemented as planned: `inputs/job-1.txt`, `inputs/job-1.expected.json`, `inputs/README.md`, plus `Extraction/JobPosting.cs` (record, `[Description]` rules, `WorkMode` with a string converter), `Extraction/JobExtractor.cs` and `Extraction/JobScorer.cs`. `/extract <path>` is in `ChatSession`. Failure paths tested on Llama: no path, missing file, and an input with no golden file. A chat turn after `/extract` had 32 input tokens, so the history wasn't touched.
- **2026-10-21:** Haiku run 1 failed. The native schema was sent (the Verbose log shows `responseFormat` with the full schema), but Haiku answered in markdown with finish reason `stop`, not JSON. It was not rejected. Changed `JobExtractor` to `useJsonSchemaResponseFormat: false` for all providers, so the schema goes into the prompt. Llama still parses with it (input +310 tokens), but invented `Remote`.
- **2026-10-21:** Haiku run 2 (the second and last paid run, approved by the user) returned correct JSON in a ```` ```json ```` fence, which `TryGetResult` rejects. Added `Extraction/JobParser.cs`: when `TryGetResult` fails, it strips one fence around the whole reply and deserializes with `AIJsonUtilities.DefaultOptions`. A new test, not in the original list: a scratch project ran `JobParser` on the exact Haiku reply (parsed, 5/5 fields, skills 10/12), plain JSON (parsed), markdown (rejected) and a broken fence (rejected). Haiku was not re-run live after this fix. Results are in `experiments.md` under "Deliverable 3". The two Haiku runs cost $0.001666. `appsettings.json` stayed on Ollama the whole time; Haiku was selected with environment variables.
