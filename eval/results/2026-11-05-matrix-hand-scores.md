# Experiment matrix: hand scores (run 1, 12 questions per cell)

Plan: `docs/plans/2026-11-05-week-5-experiment-matrix.md`, step 5. Results: `experiments.md` section 17.

Answers to read: `eval-runs/meai/matrix-<cell>-run1.md` and `matrix-<cell>-abstain.md` (git-ignored), or `eval-runs/meai/report.html`.

Rule, stop at the first fail: **facts** (every gold fact; a list missing an item fails), **support** (nothing the sources don't say), **citation** (the cited file and lines contain the gold line). Failure types: `retrieval miss`, `wrong chunk`, `generation`, `should have refused`.

## HH

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| HH | A3 |  |  |  |
| HH | N1 |  |  |  |
| HH | F5 |  |  |  |
| HH | G2 |  |  |  |
| HH | B7 |  |  |  |
| HH | A15 |  |  |  |
| HH | B6 |  |  |  |
| HH | G1 |  |  |  |
| HH | N5 |  |  |  |
| HH | C3 |  |  |  |
| HH | H6 |  |  |  |
| HH | D1 |  |  |  |

**HH: ?/12**

## FH

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| FH | A3 |  |  |  |
| FH | N1 |  |  |  |
| FH | F5 |  |  |  |
| FH | G2 |  |  |  |
| FH | B7 |  |  |  |
| FH | A15 |  |  |  |
| FH | B6 |  |  |  |
| FH | G1 |  |  |  |
| FH | N5 |  |  |  |
| FH | C3 |  |  |  |
| FH | H6 |  |  |  |
| FH | D1 |  |  |  |

**FH: ?/12**

## HL

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| HL | A3 |  |  |  |
| HL | N1 |  |  |  |
| HL | F5 |  |  |  |
| HL | G2 |  |  |  |
| HL | B7 |  |  |  |
| HL | A15 |  |  |  |
| HL | B6 |  |  |  |
| HL | G1 |  |  |  |
| HL | N5 |  |  |  |
| HL | C3 |  |  |  |
| HL | H6 |  |  |  |
| HL | D1 |  |  |  |

**HL: ?/12**

## FL

| Cell | ID | Pass | Reason (one line) | Failure type |
|---|---|---|---|---|
| FL | A3 |  |  |  |
| FL | N1 |  |  |  |
| FL | F5 |  |  |  |
| FL | G2 |  |  |  |
| FL | B7 |  |  |  |
| FL | A15 |  |  |  |
| FL | B6 |  |  |  |
| FL | G1 |  |  |  |
| FL | N5 |  |  |  |
| FL | C3 |  |  |  |
| FL | H6 |  |  |  |
| FL | D1 |  |  |  |

**FL: ?/12**

## Abstain recall

| Cell | D2 | D9 | INV | N9 | Recall /12 | E5 gated /3 |
|---|---|---|---|---|---|---|
| HH |  |  |  |  |  | 3/3 |
| FH |  |  |  |  |  | 3/3 |
| HL |  |  |  |  |  | 3/3 |
| FL |  |  |  |  |  | 3/3 |

E5 is filled in from the stored `gate` (`too_far` in all 3 runs of every cell).

## Judge vs me

A disagreement: pass with Groundedness < 4, or a `generation` fail with G ≥ 4. G per run: column G of `eval-runs/meai/matrix-<cell>-summary.md`.

### Disagreements

-

### Retrieval misses with G = 5 (not the judge's fault)

-
