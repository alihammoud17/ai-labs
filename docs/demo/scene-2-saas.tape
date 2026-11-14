# Scene 2: AiLab.Console as an MCP client of MultiTenantSaaSApi: /saas on, then one question.
# Needs the SaaS API on :5238 and the SaasMcp user-secrets. console.sh hides the startup lines with local paths.

Hide
Type "clear && docs/demo/console.sh"
Enter
Wait@90s /^You: ?$/
Show
Sleep 1s

Type "/saas on"
Enter
Hide
Wait@30s /^You: ?$/
Show
Sleep 1s

Type "Which webhook endpoints are active?"
Sleep 300ms
Enter
Hide
Wait@60s /^You: ?$/
Show
Sleep 5s
