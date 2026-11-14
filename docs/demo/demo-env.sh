# Sourced, hidden, by milestone-1.tape before scene 1, so the visible curl lines stay short.
# J: the JSON content-type header. SHOW: a jq filter that keeps the answer, the grounding status,
# the gate and one line per citation (the full response also has usage, timings and distances).
J='Content-Type: application/json'
SHOW='{answer, status: .grounding.status, gate: .retrieval.gate, citations: [.citations[]? | "[\(.n)] \(.source):\(.startLine)-\(.endLine)"]}'
export PS1='$ '
