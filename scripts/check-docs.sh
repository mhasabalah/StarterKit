#!/usr/bin/env bash
# Documentation checks for StarterKit (run from anywhere: `bash scripts/check-docs.sh`, or `make docs-check`).
#   1. Every relative markdown link resolves to an existing file or folder (anchors are not checked).
#   2. Every wiki page is linked from wiki/INDEX.md.
#   3. No forbidden placeholders or leaks (real-looking domains, local Windows paths, demo buckets).
# Exit code = number of problems (0 = clean). Needs only bash + grep + sed (works in Git Bash on Windows).
set -u
cd "$(dirname "$0")/.." || exit 1

problems=0
report() { echo "  $1"; problems=$((problems + 1)); }

mapfile -t docs < <(
  { ls README.md CLAUDE.md AGENTS.md 2>/dev/null
    find wiki deployment secrets tools/claude .github -name '*.md' -not -path '*/node_modules/*' 2>/dev/null; } | sort -u)

echo "== Relative links (${#docs[@]} files)"
for doc in "${docs[@]}"; do
  dir="$(dirname "$doc")"
  # Drop fenced code blocks, then extract ](target) links.
  sed '/^[[:space:]]*```/,/^[[:space:]]*```/d' "$doc" \
    | grep -oE '\]\([^) ]+\)' | sed -E 's/^\]\(//; s/\)$//' \
    | while IFS= read -r target; do
        case "$target" in
          http://*|https://*|mailto:*|\#*|'<'*) continue ;;
        esac
        path="${target%%#*}"
        [ -z "$path" ] && continue
        if [ ! -e "$dir/$path" ]; then
          echo "  $doc -> $target"
        fi
      done
done > /tmp/check-docs-links.$$
if [ -s /tmp/check-docs-links.$$ ]; then
  cat /tmp/check-docs-links.$$
  problems=$((problems + $(wc -l < /tmp/check-docs-links.$$)))
fi
rm -f /tmp/check-docs-links.$$

echo "== wiki pages missing from wiki/INDEX.md"
while IFS= read -r page; do
  rel="${page#wiki/}"
  [ "$rel" = "INDEX.md" ] && continue
  grep -qF "($rel" wiki/INDEX.md || report "$page"
done < <(find wiki -name '*.md' | sort)

echo "== Forbidden placeholders / leaks"
forbidden='yourdomain\.com|starterkit-demo|[A-Z]:\\(real|Users)\\|ip-172-31-|Current Production'
while IFS= read -r hit; do
  report "$hit"
done < <(grep -rnE "$forbidden" README.md CLAUDE.md AGENTS.md wiki deployment/README.md secrets/README.md tools/claude 2>/dev/null \
          | grep -v 'scripts/check-docs.sh')

if [ "$problems" -eq 0 ]; then
  echo "OK - 0 problems"
else
  echo "FOUND $problems problem(s)"
fi
exit "$problems"
