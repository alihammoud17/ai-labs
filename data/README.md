# data

## corpus/

A fixed snapshot of four documents from MultiTenantSaaSApi, the RAG corpus for stage 4 (`POST /dev/ingest` reads every `.md` under `Corpus:Path`, default `data/corpus`). These notes live outside `corpus/` so they aren't ingested.

- Source repo: `~/RiderProjects/MultiTenantSaaSApi` (https://github.com/alihammoud17/MultiTenantSaaSApi), branch `master`
- Commit: `c87cc20b9b01f163c9387a0226b938be7b9646cd` (2026-10-24), working tree clean
- Copied: 2026-10-28, byte-identical to that commit

| File | Bytes | Headings |
|---|---|---|
| `README.md` | 7,771 | 11 (`#` title + 10 `##`) |
| `docs/decisions.md` | 5,376 | 13 (10 `###` accepted decisions) |
| `docs/contracts.md` | 8,148 | 21 (`##` / `###`, with code blocks) |
| `docs/architecture.md` | 13,588 | 11 (`#` title + 10 `##`) |

About 35 KB, roughly 9,000 tokens (characters / 4). Don't edit these files to match the source repo later: a new snapshot gets a new commit hash here. Editing one on purpose is how 3.6 (skip unchanged chunks) is tested.
