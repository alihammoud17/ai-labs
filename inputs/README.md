# inputs

Real job postings used to test typed extraction (`/extract`) and, later, to compare prompts.

| File | What it is |
|---|---|
| `job-N.txt` | A real posting, copied as is |
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

## Planned inputs

1. Long and messy: `job-1.txt` (done)
2. Clean
3. French
4. Missing details
5. Tricky: hybrid wording, salary, or two roles in one post
