# Scene 1: POST /ask on AiLab.Rag (Haiku 5.5). An answerable question, then one the docs don't cover.
# Needs AiLab.Rag running on :5074 with the heading chunks ingested (see README.md in this folder).

Type `curl -s localhost:5074/ask -H "$J" -d '{"question":"Is the Stripe integration live?"}' | jq "$SHOW"`
Sleep 300ms
Enter
Hide
Wait@60s
Show
Sleep 5s

Hide
Type "clear"
Enter
Show
Type `curl -s localhost:5074/ask -H "$J" -d '{"question":"How long is a refresh token valid?"}' | jq "$SHOW"`
Sleep 300ms
Enter
Hide
Wait@60s
Show
Sleep 5s
