# Experiment matrix: hand scores (run 1, 12 questions per cell)

Plan: `docs/plans/2026-11-05-week-5-experiment-matrix.md`, step 5. Results: `experiments.md` section 17.

*Scored by Claude on 2026-11-07, at the user's request, using the rule below. On the two close calls (HH H6 and FL N9 run 1) the user chose the strict reading.*

Answers to read: `eval-runs/meai/matrix-<cell>-run1.md` and `matrix-<cell>-abstain.md` (git-ignored), or `eval-runs/meai/report.html`.

Rule, stop at the first fail: **facts** (every gold fact; a list missing an item fails), **support** (nothing the sources don't say), **citation** (the cited file and lines contain the gold line). Failure types: `retrieval miss`, `wrong chunk`, `generation`, `should have refused`.

## HH (heading, Haiku)

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| HH | A3 | pass | `2026-03-18`, cites contracts.md:43-65 | |
| HH | N1 | pass | no, names the process and PostgreSQL, both sources cited | |
| HH | F5 | pass | `Jwt__Secret`, cites README.md:65-76 | |
| HH | G2 | pass | 10 per minute, shared by the account-recovery routes, not with login | |
| HH | B7 | pass | all five limits, cites architecture.md:40-46 | |
| HH | A15 | pass | all six checks, cites contracts.md:67-78; the extra HMAC check is cited too | |
| HH | B6 | pass | 5 per 5 minutes per user and PostgreSQL 16+, each from its own source | |
| HH | G1 | pass | gives both versions, one per contract | |
| HH | N5 | pass | JSON file, single-instance only, cites decisions.md:33-35 and architecture.md:79-84 | |
| HH | C3 | pass | no lockout, cited | |
| HH | H6 | fail | `202` and no tokens, but no `pending_verification` status (the passIf asks for it) | generation |
| HH | D1 | pass | names `Jwt:ExpirationMinutes` and says no default is given | |

**HH: 11/12**

## FH (fixed, Haiku)

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| FH | A3 | pass | `2026-03-18`, cites contracts.md:39-82 | |
| FH | N1 | pass | no, names the process and PostgreSQL | |
| FH | F5 | pass | `Jwt__Secret`; the double-underscore note is in README line 67 | |
| FH | G2 | pass | 10 per minute, shared by the account-recovery routes | |
| FH | B7 | fail | only the monthly limiter; the window cuts line 46 in two, and the half holding the four route limits (architecture.md:40-46) wasn't in the top 5. Haiku said the rest isn't covered, nothing invented | retrieval miss |
| FH | A15 | pass | all six checks, cites contracts.md:39-82 | |
| FH | B6 | fail | says no MFA limit is documented; its chunk wasn't in the top 5 (as step 2 predicted) | retrieval miss |
| FH | G1 | fail | only `2026-03-18`; the webhook contract's chunk wasn't in the top 5 (as step 2 predicted) | retrieval miss |
| FH | N5 | pass | JSON file, single-instance only, cites architecture.md:82-90 | |
| FH | C3 | pass | no lockout, plus the 10-per-minute budget | |
| FH | H6 | pass | `202 Accepted`, `pending_verification`, no tokens, cites architecture.md:36-41 | |
| FH | D1 | pass | names `Jwt:ExpirationMinutes` and says no default is given | |

**FH: 9/12**

## HL (heading, llama3.2)

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| HL | A3 | fail | right value, but cites contracts.md:3-5, which doesn't contain it | wrong chunk |
| HL | N1 | pass | no, names self and PostgreSQL, cites architecture.md:86-90 | |
| HL | F5 | pass | `Jwt__Secret`, cites README.md:65-76 | |
| HL | G2 | fail | says 5 attempts per 5 minutes (that's the MFA limit) | generation |
| HL | B7 | pass | all five limits; [1] is architecture.md:40-46 | |
| HL | A15 | pass | all six checks, cites contracts.md:67-78 | |
| HL | B6 | fail | MFA right, but PostgreSQL 16 cites architecture.md:79-84, which doesn't say 16 | wrong chunk |
| HL | G1 | fail | only `2026-03-18`, though both contracts were in the top 5 | generation |
| HL | N5 | pass | JSON file, not for several instances, cites architecture.md:79-84 | |
| HL | C3 | pass | no lockout, cited | |
| HL | H6 | fail | "202 with no tokens", no `pending_verification` | generation |
| HL | D1 | fail | names the setting but doesn't say the docs give no value | generation |

**HL: 6/12**

## FL (fixed, llama3.2)

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| FL | A3 | fail | right value, but cites decisions.md:1-31, which doesn't contain it | wrong chunk |
| FL | N1 | fail | says no, but doesn't name what is checked (the passIf asks for it) | generation |
| FL | F5 | fail | right name, but cites decisions.md:27-47, which doesn't contain it | wrong chunk |
| FL | G2 | fail | answers about MFA and abstains, though architecture.md:40-46 was rank 1 | generation |
| FL | B7 | fail | only the monthly limiter, plus an invented "10 per minute for BillingService" | retrieval miss |
| FL | A15 | fail | 1 of 6 checks, though the whole list was in rank 1 | generation |
| FL | B6 | fail | abstained ("notInDocs"); the MFA chunk wasn't in the top 5 | retrieval miss |
| FL | G1 | fail | only `2026-03-18`; the webhook contract wasn't in the top 5 | retrieval miss |
| FL | N5 | fail | says it *can* run as several instances, the opposite of the source | generation |
| FL | C3 | pass | no lockout, cited | |
| FL | H6 | fail | `202` and `pending_verification`, but doesn't say no tokens | generation |
| FL | D1 | fail | names the setting but doesn't say no default is given | generation |

**FL: 1/12**

## Abstain recall

| Cell | D2 | D9 | INV | N9 | Recall /12 | E5 gated /3 |
|---|---|---|---|---|---|---|
| HH | 3/3 | 3/3 | 3/3 | 3/3 | 12/12 | 3/3 |
| FH | 3/3 | 3/3 | 3/3 | 3/3 | 12/12 | 3/3 |
| HL | 1/3 | 0/3 | 3/3 | 2/3 | 6/12 | 3/3 |
| FL | 1/3 | 0/3 | 3/3 | 3/3 | 7/12 | 3/3 |

E5 is filled in from the stored `gate` (`too_far` in all 3 runs of every cell).

- **Haiku, both cells:** every run says the docs don't cover it. Most also mention the nearby facts the docs do have (rotation, tenant-scoped audit entries), with citations, which is fine.
- **HL D2:** run 2 invents "30 minutes", run 3 invents "1 hour". **HL N9 run 3** describes audit entries and never says the retention isn't covered.
- **FL D2:** run 1 implies the refresh token follows `Jwt:ExpirationMinutes`, and run 3 is an unrelated sentence. **FL N9 run 1** is just the string "notInDocs": counted as an abstain (strict reading, chosen by the user), but a user couldn't read it.
- **D9, both llama cells, 0/3:** each run defines multi-tenancy from general knowledge, sometimes with a citation attached. That's exactly the failure the abstain bar exists to catch.

## Judge vs me

A disagreement: pass with Groundedness < 4, or a `generation` fail with G ≥ 4. G per run: column G of `eval-runs/meai/matrix-<cell>-summary.md`.

### Disagreements

None of the passes has G < 4. Eight `generation` fails scored G ≥ 4:

- HH H6: I fail (no `pending_verification`), G = 5
- HL G1: I fail (one contract of two), G = 4
- HL H6: I fail (no `pending_verification`), G = 5
- HL D1: I fail (doesn't say there's no default), G = 4
- FL N1: I fail (doesn't name what is checked), G = 5
- FL A15: I fail (1 of 6 checks), G = 4
- FL H6: I fail (doesn't say no tokens), G = 4
- FL D1: I fail (doesn't say there's no default), G = 4

All eight are **omissions**: what the answer says is supported, but something the gold answer needs is missing. Groundedness checks support, not completeness, so it can't see them. Where the answer said something *false* (HL G2, FL N5), the judge did drop to 3. The one wrong abstain (FL G2) wasn't scored at all, because the wrapper skips abstained runs.

### Retrieval misses with G = 5 (not the judge's fault)

- FH B6, FH G1, FL G1: the answer is supported by what was sent; the missing fact never reached the model.
- FH B7 (G = 4) and FL B7 (G = 3): same cause. Recall@5 still counted them as hits, because the window that *was* sent contains line 46, but only its second half.

### Wrong chunk with G = 5

- HL A3, FL A3, FL F5, HL B6: the value is right, the citation points at a chunk that doesn't hold it. The judge reads the whole context, not the cited source, so it can't catch these. `CitationCheck` only checks that `[n]` was sent, not that it contains the claim.
