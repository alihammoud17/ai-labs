# Scene 3: the same webhooks ticket through /agent (the model picks its lookups) and /workflow
# (sort -> code runs the lookups -> write). Waits for the model are hidden; the totals lines show the real time.
# The console restarts on a cleared screen before each (hidden, no model calls), so the step lines start at the top.

Hide
Type "/exit"
Enter
Wait@30s
Type "clear && docs/demo/console.sh"
Enter
Wait@90s /^You: ?$/
Show

Type "/agent Our webhooks stopped arriving since yesterday."
Sleep 300ms
Enter
Hide
Wait+Screen@90s /step 1\.1/
Show
Sleep 2s
Hide
Wait@180s /^You: ?$/
Show
Sleep 5s

Hide
Type "/exit"
Enter
Wait@30s
Type "clear && docs/demo/console.sh"
Enter
Wait@90s /^You: ?$/
Show

Type "/workflow Our webhooks stopped arriving since yesterday."
Sleep 300ms
Enter
Hide
Wait+Screen@60s /step 2 collect/
Show
Sleep 2s
Hide
Wait@90s /^You: ?$/
Show
Sleep 6s

Hide
Type "/exit"
Enter
