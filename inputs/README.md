# inputs

Job postings used to test typed extraction (`/extract`) and to compare prompts (`/extract v1|v2 <path>`, prompts in `../prompts/`).

| File | What it is |
|---|---|
| `job-N.txt` | A real posting, copied as is (job-1 to job-3), or an edited version of one (job-4, job-5) |
| `job-N.expected.json` | My hand-written golden answer for that posting, written before running any model |

Run one from the repo root with `/extract inputs/job-1.txt`. If `job-1.expected.json` sits next to it, the app scores the result.

## Extraction rules

The golden answers follow these rules, and so should the model:

- **Title:** the job title without a sub-brand suffix (drop " - Acom").
- **Company:** the hiring company named in the post, not a sub-brand in the title.
- **Location:** "City, Country" when stated.
- **Work mode:** `OnSite`, `Hybrid` or `Remote`, or `null` when the post doesn't say. Never guess.
- **Required skills:** technologies from the Requirements section only, not "Bonus Points" or nice-to-have. CV-keyword level ("Azure", not every Azure service), at most about 12.
- **Years of experience:** the minimum number stated, even if softened with "ideally"; `null` if not stated.

## Scoring

- **Title, company, location, work mode, years:** exact match per field.
- **Skills:** recall, meaning how many golden skills appear in the model's list, accepting wording a human would treat as the same. The app's count is strict (same name, ignoring case), so it's a lower bound; check the listed misses by hand.

## Inputs

Three real posts, plus two edited versions of them that test specific rules. More real LinkedIn posts would add little: most are messy, over-demanding and very similar.

1. `job-1.txt`: long and messy, with a sub-brand in the title
2. `job-2.txt`: trading firm, with a `LI-Onsite` tag and many "an advantage" items
3. `job-3.txt`: French
4. `job-4.txt`: tests "never guess". It's job-2 with the "4+ years" requirement and the `LI-Onsite` tag removed, so the post states no work mode and no years. Phrases like "an experienced developer" and "daily breakfast and lunch" are kept: they hint, but they don't state anything. Expected: `workMode` and `yearsOfExperience` are `null`.
5. `job-5.txt`: tests "extract the first role". It's job-1 unchanged, followed by "We are also hiring:" and a shorter second role: job-3's title and technical skills, translated to English. Expected: the same answer as job-1, with no skills from the second role.
