#!/bin/bash
# Keyword (OR) search over the heading chunks, with each hit's cosine distance to the question's real vector.
# Usage: eval/queries/cosine_distance.sh ["question"]   (default: the JWT question)
# Needs Ollama (nomic-embed-text) and the Postgres container.

Q=${1:-"Which environment variable sets the JWT secret?"}

# python builds the JSON body, so quotes and accents in the question are escaped properly
V=$(python3 -c 'import json,sys;print(json.dumps({"model":"nomic-embed-text","input":sys.argv[1]}))' "$Q" \
  | curl -s localhost:11434/api/embed -d @- \
  | python3 -c "import json,sys;print(json.dumps(json.load(sys.stdin)['embeddings'][0]).replace(' ',''))")

# Inside the SQL string a single quote is doubled (l'inscription -> l''inscription)
QSQL=${Q//\'/\'\'}

docker exec ai-labs-vectors-1 psql -U ailab -d ailab -c "
SELECT \"Key\", \"Source\", \"StartLine\", \"EndLine\",
       ts_rank_cd(to_tsvector('english', \"Text\"), q) AS \"Rank\",
       round((\"Embedding\" <=> v)::numeric, 4) AS \"Distance\"
FROM corpus,
     to_tsquery('english', replace(plainto_tsquery('english', '$QSQL')::text, '&', '|')) AS q,
     (SELECT '$V'::vector AS v) AS question
WHERE \"Chunker\" = 'heading' AND to_tsvector('english', \"Text\") @@ q
ORDER BY \"Rank\" DESC LIMIT 10;"
