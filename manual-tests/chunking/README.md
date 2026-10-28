# Manual test: chunkers and chunk metadata (stage 4, 3.1–3.3)

Run everything from the repo root. Part A needs nothing running. Part B needs Docker (`colima start`, `docker compose up -d`) and Ollama.

For each step: **predict first**, then run and compare.

- `sample.md`: a small file with one case per rule (text before any heading, an empty section, a skipped level, a `#` inside a code fence, a section too long for one chunk).
- `chunk.cs`: a .NET 10 file-based script that prints every chunk:

```bash
dotnet run manual-tests/chunking/chunk.cs -- <file.md> [fixed|heading] [maxChars overlap minTrail]
```

Without the three numbers it uses `ChunkingOptions.Default` (2000 / 200 / 100). The sample uses 300 / 50 / 30 so that a few lines make several chunks.

## A. The chunkers (offline)

### A1. Heading chunker on the sample

```bash
dotnet run manual-tests/chunking/chunk.cs -- manual-tests/chunking/sample.md heading 300 50 30
```

Predict: how many chunks? What happens to `## Empty section`, `# Sign the body` and `#### Skipped level`?

Expected: **6 chunks**.

| # | Lines | Heading | What it shows |
|---|---|---|---|
| 1 | 1–1 | (none) | Text before the first heading is its own chunk |
| 2 | 5–7 | Billing Guide > Overview | `# Billing Guide` has no body of its own, so it makes no chunk; the path is prepended to the text |
| 3 | 11–13 | Billing Guide > Webhooks > Retries | The Retries body (384 characters) is over 300, so it's split… |
| 4 | 13–13 | Billing Guide > Webhooks > Retries | …and the second piece keeps the heading. It starts with "keep the original event id…": the 50-character overlap |
| 5 | 15–20 | Billing Guide > Webhooks > Retries > Headers | `# Sign the body` is inside a ```` ```bash ```` fence, so it's body text, not a heading |
| 6 | 26–28 | Billing Guide > Limits > Skipped level | `## Empty section` makes no chunk; `####` under `##` (skipped level) still nests |

Also check: the first piece of Retries starts at line 11 (the heading line), and the second at line 13, where its own text starts.

### A2. Fixed chunker on the same file

```bash
dotnet run manual-tests/chunking/chunk.cs -- manual-tests/chunking/sample.md fixed 300 50 30
```

Predict: does a chunk cut through the code block? What `heading` does the last chunk get?

Expected: **3 chunks** of the raw markdown (heading lines included, no path in front).

- Chunk 1 (lines 1–13, 300 characters) mixes four sections, and its heading is `(none)`: the heading is taken from where the chunk **starts**.
- Chunk 2 ends with an opening ```` ```bash ```` and no closing fence: the code block is cut in half.
- Chunk 3 holds Headers, Empty section and Limits, but its heading says `Retries`. With fixed chunks, the heading is a hint, not the topic.
- Each chunk after the first starts with the last ~50 characters of the previous one, cut at a word.

This is the trade-off 3.5 measures on the real corpus.

### A3. Short last piece merged

```bash
dotnet run manual-tests/chunking/chunk.cs -- manual-tests/chunking/sample.md heading 300 50 100
```

Expected: **5 chunks**. Retries is one 420-character chunk. The second piece would have added fewer than 100 new characters (the rest is overlap), so it was merged into the first. That chunk is over `MaxChars`, which is allowed by up to `MinTrailChars`.

### A4. Invalid options

```bash
dotnet run manual-tests/chunking/chunk.cs -- manual-tests/chunking/sample.md heading 300 150 30
```

Expected: `ArgumentOutOfRangeException`: "The overlap must be less than half of the chunk size (300)…". Also try `300 0 30` (zero refused).

### A5. Windows line ends

```bash
sed 's/$/'$'\r''/' manual-tests/chunking/sample.md > /tmp/sample-crlf.md
dotnet run manual-tests/chunking/chunk.cs -- /tmp/sample-crlf.md heading 300 50 30
```

Expected: the same 6 chunks, the same line numbers and **the same hashes** as A1. `\r\n` is turned into `\n` before anything else, so the file's line ends don't change a chunk's hash. Otherwise, re-ingesting the same file after a checkout on Windows would embed everything again.

### A6. The real corpus, default sizes

```bash
dotnet run manual-tests/chunking/chunk.cs -- data/corpus/docs/contracts.md heading
dotnet run manual-tests/chunking/chunk.cs -- data/corpus/docs/contracts.md fixed
```

Expected: heading 15 chunks, fixed 5.

- Heading chunk 3 is `… > Internal billing subscription events > Authentication` (lines 17–41, about 1,560 characters), whole, with the HMAC table and its code block.
- No chunk for `## Internal billing subscription events`: it goes straight to `### Endpoint`.
- Fixed chunks 2 and 4 start in the middle of a table (`|  The producer side…`, `| | suspend…`).

Look for these yourself: which fixed chunk would answer "How are internal billing callbacks authenticated?" Is the answer in one chunk or split across two?

### A7. The automated tests

```bash
dotnet test --filter "FullyQualifiedName~Chunk"
```

Expected: all pass (`ChunkingOptionsTests`, `FixedSizeChunkerTests`, `HeadingChunkerTests`, `CorpusChunkingTests`, `TextChunkTests`). Break a rule and watch a test fail. For example, in `MarkdownText.FindHeadings`, comment out the `continue;` at the end of the `if (fence is not null)` block (so lines inside a fence are checked for headings) and run `HeadingChunkerTests` again: `A_hash_inside_a_code_fence_is_not_a_heading` fails. Put it back afterwards.

## B. Metadata in Postgres (3.2)

### B1. The table

```bash
docker compose exec vectors psql -U ailab -d ailab -c '\d corpus'
```

Expected: columns `Key`, `Text`, `Source`, `Chunker`, `Position`, `Heading` (the only nullable text column), `StartLine`, `EndLine`, `ContentHash`, `Embedding vector(768)`. Indexes: the primary key, btree on `Chunker` and `Source`, hnsw on `Embedding`.

### B2. Seed and read the rows

```bash
dotnet run --project AiLab.Rag            # in another terminal
curl -s -X POST localhost:5074/dev/seed
docker compose exec vectors psql -U ailab -d ailab -c \
  'SELECT "Key", "Chunker", "Position", "StartLine", "EndLine", left("ContentHash", 12) FROM corpus ORDER BY "Position";'
```

Expected: 10 rows, keys `lines:similarity-sentences.txt:<line>`, `Position` = `StartLine` = `EndLine` = the line number, and `Chunker` = `lines`. Seed again: still 10 (same keys, so rows are replaced).

### B3. The hash is the SHA-256 of the text

```bash
docker compose exec vectors psql -U ailab -d ailab -tA -c \
  "SELECT \"ContentHash\" FROM corpus WHERE \"Key\" = 'lines:similarity-sentences.txt:4';"
printf '%s' "Caching is enabled." | shasum -a 256
```

Expected: the same 64 hex characters (`6354f52f5269…`). `printf '%s'` matters: `echo` adds a newline, which gives a different hash.

### B4. Ingest the corpus and filter by chunker (3.4)

```bash
curl -s -X POST "localhost:5074/dev/ingest?chunker=both"
curl -s "localhost:5074/dev/chunks?chunker=heading&source=docs/contracts.md"
curl -s "localhost:5074/search?q=How%20are%20internal%20billing%20callbacks%20authenticated%3F&top=3&chunker=heading"
```

Expected: the ingest reports fixed 21 chunks and heading 50, each with a real `embeddingTokens` count a few percent below the characters / 4 estimate. `/dev/chunks` lists the 15 sections of `contracts.md` in order, as in A6. The search returns only `heading` chunks, each with its `heading`, `position` and line range. `rag.http` has the same requests, plus the other four questions for 3.5.
