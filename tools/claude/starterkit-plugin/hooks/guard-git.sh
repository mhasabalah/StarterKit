#!/usr/bin/env bash
# PreToolUse (Bash/PowerShell): StarterKit rule "never commit" - the user reviews the diff and commits.
# Blocks `git commit` / `git push` issued by the agent (exit 2 = deny, stderr is shown to Claude).
# Set STARTERKIT_ALLOW_GIT_COMMIT=1 in the environment to allow it for a session where the user asked for commits.
set -u
[ "${STARTERKIT_ALLOW_GIT_COMMIT:-0}" = "1" ] && exit 0

input="$(cat)"
# Only look at the command field; good enough without jq (not installed everywhere, e.g. Git Bash on Windows).
command="$(printf '%s' "$input" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(.*\)".*/\1/p' | head -n 1)"

if printf '%s' "$command" | grep -Eq '(^|[;&|][[:space:]]*)git([[:space:]]+-[^[:space:]]+([[:space:]]+[^-[:space:]][^[:space:]]*)?)*[[:space:]]+(commit|push)([^[:alnum:]-]|$)'; then
  echo "Blocked by StarterKit guardrail: agents never commit or push in this repo (CLAUDE.md > Rules). Leave the changes in the working tree and tell the user they are ready to review. If the user explicitly asked you to commit, ask them to set STARTERKIT_ALLOW_GIT_COMMIT=1 for this session." >&2
  exit 2
fi
exit 0
