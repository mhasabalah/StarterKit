#!/usr/bin/env bash
# PostToolUse (Edit/Write): fast grep for StarterKit anti-patterns in the edited .cs file.
# Never blocks the edit (it already happened); exit 2 feeds the warning back to Claude so it fixes the code.
# The architecture tests (dotnet test src/Api.Tests) remain the authoritative check.
set -u

input="$(cat)"
file="$(printf '%s' "$input" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
# JSON-escaped Windows backslashes -> forward slashes (C:/x/y.cs works in Git Bash and on Linux paths are unchanged).
file="$(printf '%s' "$file" | tr '\\' '/' | sed 's#//*#/#g')"

case "$file" in
  *.cs) ;;
  *) exit 0 ;;
esac
case "$file" in
  */bin/*|*/obj/*|*/Migrations/*) exit 0 ;;
esac
[ -f "$file" ] || exit 0

is_test=0
case "$file" in *Tests/*|*Tests.cs|*IntegrationTests*|*E2ETests*) is_test=1 ;; esac

warnings=""
add() { warnings="${warnings}
- $1"; }

if [ "$is_test" = "0" ]; then
  if grep -qE '\.IgnoreQueryFilters\([[:space:]]*\)' "$file"; then
    add "parameterless IgnoreQueryFilters() drops tenant isolation - use IgnoreNonIsolationFilters() / IgnoreBranchScope() (TenantIsolationArchitectureTests)."
  fi
  if grep -qE '\[FromServices\]' "$file"; then
    add "[FromServices] is not needed with Carter/minimal APIs - take the service as a plain parameter."
  fi
  if grep -qE '\.Version[[:space:]]*(\+\+|\+=|=[^=>])' "$file"; then
    add "do not set Version by hand - AuditableEntityInterceptor bumps it; use dbContext.ExpectVersion(entity, command.Version)."
  fi

  case "$file" in
    */Features/*|*Handler.cs|*EndPoint.cs)
      if grep -qE '\b(IPublishEndpoint|IBus)\b' "$file"; then
        add "publishing from feature code bypasses the outbox - raise AddIntegrationEvent(...) on the aggregate instead (wiki/Architecture/Messaging.md)."
      fi ;;
  esac

  case "$file" in
    *Handler.cs|*Job.cs|*Consumer.cs|*Orchestrator.cs)
      if grep -qE 'DateTime\.(UtcNow|Now)\b' "$file"; then
        add "inject TimeProvider instead of DateTime.UtcNow in handlers/jobs/consumers (testable time)."
      fi ;;
  esac

  case "$file" in
    *Job.cs|*Consumer.cs|*Orchestrator.cs)
      if grep -qE 'IServiceScopeFactory|CreateScope\(' "$file"; then
        add "do DB work through IUnitOfWorkRunner.RunAsync instead of hand-rolled scopes, so domain events are published (BackgroundJobEventDispatchTests)."
      fi ;;
  esac
fi

if [ -n "$warnings" ]; then
  printf 'StarterKit guardrail warnings for %s:%s\nFix these, or explain why the rule does not apply here.\n' "$file" "$warnings" >&2
  exit 2
fi
exit 0
