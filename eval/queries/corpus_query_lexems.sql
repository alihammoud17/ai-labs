
/* 
 * This query is used to count the number of rows in the corpus table where the "Chunker" column is 'heading' and the "Text" column matches the given search query using full-text search.
 * It helps in understanding how many headings in the corpus contain the specified query.
 * All words in the query must be present in the "Text" column for a row to be counted, as indicated by the use of plainto_tsquery.
 */

SELECT count(*) FROM corpus WHERE "Chunker" = 'heading' AND to_tsvector('english', "Text") @@ plainto_tsquery('english', 'Which environment variable sets the JWT secret?');


/* 
 * This query is used to retrieve the top 10 rows from the corpus table where the "Chunker" column is 'heading' and the "Text" column matches the given search query using full-text search.
 * It also calculates the rank of each matching row based on how well it matches the search query.
 * The results are ordered by rank in descending order, allowing you to see the most relevant headings first.
 * Unlike the previous query, this one uses to_tsquery with a replacement of '&' with '|' to allow for more flexible matching of the search terms.
 */

SELECT "Key", "Source", "StartLine", "EndLine",
       ts_rank_cd(to_tsvector('english', "Text"), q) AS "Rank"
FROM corpus,
     to_tsquery('english', replace(plainto_tsquery('english', 'Which environment variable sets the JWT secret?')::text, '&', '|')) AS q
WHERE "Chunker" = 'heading'
  AND to_tsvector('english', "Text") @@ q
ORDER BY "Rank" DESC
LIMIT 10;


/* 
 * This query is used to retrieve the top 10 rows from the corpus table where the "Chunker" column is 'heading' and the "Text" column matches the given search query using full-text search.
 * It also calculates the rank of each matching row based on how well it matches the search query, as well as the cosine distance between the embedding of each matching row and a specific question embedding.
 * The results are ordered by rank in descending order, allowing you to see the most relevant headings first, along with their distance to the question embedding.
 * Unlike the previous query, this one has an additional column for the cosine distance, which can be useful for understanding the semantic similarity between the headings and the question.
 */

SELECT "Key", "Source", "StartLine", "EndLine",
       ts_rank_cd(to_tsvector('english', "Text"), q) AS "Rank",
       "Embedding" <=> v AS "Distance"
FROM corpus,
     to_tsquery('english', replace(plainto_tsquery('english', 'Which environment variable sets the JWT secret?')::text, '&', '|')) AS q,
     (SELECT "Embedding" AS v FROM corpus WHERE "Key" = 'heading:README.md:6') AS question
WHERE "Chunker" = 'heading'
  AND to_tsvector('english', "Text") @@ q
ORDER BY "Rank" DESC
LIMIT 10;