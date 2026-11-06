#!/usr/bin/env bash
# PreToolUse (Bash/PowerShell): StarterKit git limits (CLAUDE.md > Rules > Git).
# Agents may commit, push and open PRs on FEATURE branches when the user asks. This hook denies (exit 2, stderr is
# shown to Claude) what must stay with the user:
#   - a commit while on a protected branch (master, main, develop), or a command that switches to one and commits
#   - a push to a protected branch, any force-push (--force, -f, --force-with-lease, +refspec), deleting a remote branch
#   - skipping hooks (--no-verify, commit -n), merging a PR (gh pr merge), git reset --hard
# Set STARTERKIT_ALLOW_PROTECTED_GIT=1 for a session in which the user explicitly wants one of these.
set -u
[ "${STARTERKIT_ALLOW_PROTECTED_GIT:-0}" = "1" ] && exit 0

input="$(cat)"
# Only the command field; good enough without jq (not installed everywhere, e.g. Git Bash on Windows).
command="$(printf '%s' "$input" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\(.*\)".*/\1/p' | head -n 1)"
[ -z "$command" ] && exit 0

protected='(master|main|develop)'
# "git <global options> <verb>" at the start of the command or after ; & | ( or a newline escape.
git_verb() {
  printf '%s' "$command" | grep -Eq "(^|[;&|(]|\\\\n)[[:space:]]*git([[:space:]]+-[^[:space:]]+([[:space:]]+[^-[:space:]][^[:space:]]*)?)*[[:space:]]+$1([^[:alnum:]-]|\$)"
}
deny() {
  echo "Blocked by StarterKit git limits (CLAUDE.md > Rules > Git): $1 Work on a feature branch and open a PR; the user reviews and merges. If the user explicitly asked for this, they can set STARTERKIT_ALLOW_PROTECTED_GIT=1 for the session." >&2
  exit 2
}

branch="$(git -C "${CLAUDE_PROJECT_DIR:-$PWD}" rev-parse --abbrev-ref HEAD 2>/dev/null || true)"

if git_verb commit; then
  printf '%s' "$branch" | grep -Eqx "$protected" && deny "committing on '$branch' is not allowed."
  printf '%s' "$command" | grep -Eq "git[[:space:]]+(checkout|switch)[[:space:]]+$protected([^[:alnum:]/_-]|\$)" &&
    deny "switching to a protected branch and committing is not allowed."
  printf '%s' "$command" | grep -Eq -- '--no-verify|commit([[:space:]]+-[^[:space:]]+)*[[:space:]]+-[[:alpha:]]*n' &&
    deny "skipping commit hooks (--no-verify / -n) is not allowed."
fi

if git_verb push; then
  printf '%s' "$command" | grep -Eq -- '--force|--force-with-lease|--force-if-includes|(^|[[:space:]])-[[:alpha:]]*f([[:space:]]|$)|[[:space:]]\+[^[:space:]]' &&
    deny "force-pushing is not allowed."
  printf '%s' "$command" | grep -Eq -- '--delete|(^|[[:space:]])-d([[:space:]]|$)|[[:space:]]:[^[:space:]]' &&
    deny "deleting a remote branch is not allowed."
  printf '%s' "$command" | grep -Eq -- '--no-verify' && deny "skipping push hooks (--no-verify) is not allowed."
  printf '%s' "$command" | grep -Eq "push([[:space:]]+[^;&|]*)?[[:space:]:/]$protected([^[:alnum:]/_-]|\$)" &&
    deny "pushing to a protected branch is not allowed."
  # A bare "git push" pushes the current branch.
  printf '%s' "$branch" | grep -Eqx "$protected" && deny "pushing while on '$branch' is not allowed."
fi

printf '%s' "$command" | grep -Eq '(^|[;&|(]|\\n)[[:space:]]*gh[[:space:]]+pr[[:space:]]+merge' &&
  deny "merging a pull request is the user's decision."
git_verb reset && printf '%s' "$command" | grep -Eq -- '--hard' && deny "git reset --hard discards work."

exit 0
