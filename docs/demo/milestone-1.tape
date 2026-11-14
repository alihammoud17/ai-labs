# Renders docs/demo/milestone-1.gif (1000x600). Run from the repo root:
#   vhs docs/demo/milestone-1.tape
# Prerequisites, cost and checks: docs/demo/README.md. Every model call goes to Haiku 5.5.

Output docs/demo/milestone-1.gif
# Every frame as text, for the secret check in README.md (git-ignored).
Output docs/demo/milestone-1.txt

Require curl
Require jq

Set Shell "bash"
Set FontSize 16
Set Width 1000
Set Height 600
Set Padding 16
Set Framerate 15
Set TypingSpeed 25ms
Set WaitPattern /^\$ ?$/

Env Chat__Provider "anthropic"
Env Chat__Model "claude-haiku-5-5"

Hide
Type "source docs/demo/demo-env.sh && clear"
Enter
Sleep 500ms
Show

Source docs/demo/scene-1-ask.tape
Source docs/demo/scene-2-saas.tape
Source docs/demo/scene-3-agent.tape
