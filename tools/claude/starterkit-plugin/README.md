# StarterKit Claude Code plugin

Project-specific skills, a guardrail reviewer agent and safety hooks for the StarterKit modular monolith. Each skill
packages one kind of recurring change into a checklist with templates taken from the real Catalog code, and ends
with a verification step that runs the architecture tests.

## Install

This plugin is enabled automatically in this repository. The committed `.claude/settings.json` registers the
repo-local marketplace (`.claude-plugin/marketplace.json`) and enables `starterkit@starterkit`. Trust the folder when
Claude Code asks, or run:

```
/plugin marketplace add ./
/plugin install starterkit@starterkit
```

## Skills (`/starterkit:<name>`)

| Skill | Use it to |
|---|---|
| `new-module` | Scaffold a module (4 projects, DbContext, registration, host and architecture-test wiring) |
| `add-aggregate` | Add an entity or aggregate (model, events, exceptions, EF config, repository) |
| `add-feature` | Add a command or query slice (contract, validator + handler, Carter endpoint, auth, concurrency) |
| `add-migration` | Create, review and apply an EF Core migration (+ RLS for tenant tables) |
| `add-integration-event` | Publish a cross-module fact via the outbox and consume it idempotently |
<!--#if (agentic)-->
| `add-agent-tool` | Add an AI agent or a typed agent tool to a module (risk, permission, idempotency, triggers, scripted tests) |
<!--#endif-->
| `add-background-job` | Add a tenant-aware Hangfire job that publishes its domain events |
| `add-permission` | Add a platform or tenant permission and protect endpoints |
| `add-email-template` | Add a transactional email (enum, model, subject, Liquid) and send it |
| `write-tests` | Write unit, integration, E2E and architecture tests, and run a single test |
| `review` | Run an interactive senior review (Architecture → Quality → Tests → Performance) plus a guardrail pass |
| `new-project` | Bootstrap a new product from the template |
| `update-docs` | Put docs in the right place, keep INDEX/README/CLAUDE.md in sync, run the link checker |

## Agent

`architecture-reviewer` is a read-only reviewer. It checks the working-tree diff against the StarterKit guardrails:
boundaries, tenancy, outbox, jobs, auth, naming, concurrency and localization.

## Hooks

| Event | Script | Behaviour |
|---|---|---|
| PreToolUse (Bash/PowerShell) | `hooks/guard-git.sh` | Git limits (CLAUDE.md > Rules > Git): blocks a commit while on `master`/`develop`, a push to them, any force-push, `--no-verify`, `gh pr merge`, deleting a remote branch, and `git reset --hard`. Commits and PRs on feature branches are allowed. `STARTERKIT_ALLOW_PROTECTED_GIT=1` lifts it for a session. |
| PostToolUse (Edit/Write `*.cs`) | `hooks/check-csharp-guardrails.sh` | Warns on: parameterless `IgnoreQueryFilters()`, `[FromServices]`, publishing from features, `DateTime.UtcNow` in handlers/jobs, hand-rolled scopes in jobs, manual `Version` writes. |

The hooks need `bash`: Git Bash on Windows, or the system shell on Linux and macOS.

## Maintaining

- When the Catalog reference code changes, update the matching `skills/*/references/templates.md`.
- Validate after edits with `claude plugin validate tools/claude/starterkit-plugin` and `claude plugin validate .`.
