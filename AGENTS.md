# AGENTS.md

**`CLAUDE.md` is the single source of truth** for this repository's conventions, architecture rules and guardrails.
Read it in full before changing code. This file only repeats the rules an agent must never break, for tools that
don't load `CLAUDE.md`.

## Non-negotiable rules

1. **Commit and open PRs only when the user asks, and only on a feature branch** (`feature/...`, `fix/...`). Never
   commit on, push to or merge into `master` / `develop`. Never force-push, skip hooks or merge a PR: the user reviews
   and merges. Build and tests must pass first (`CLAUDE.md` > Rules > Git).
2. After backend/.NET code changes, run `dotnet build StarterKit.slnx` and fix all errors. Then run the relevant tests
   and `dotnet test src/Api.Tests`, the architecture guardrails. Documentation-only work, including documentation
   application code, uses documentation checks and site builds; do not build/start the API or run .NET tests for it.
3. Modules talk only through `ISender` + the other module's `.Contracts`, or through integration events
   (transactional outbox). Never reference another module's implementation. Users come only from
   `UserDirectory.Contracts`.
4. Tenant data:
   - every entity in an `ITenantDataContext` is `IMustHaveTenant`
   - `ApplyTenantFilters(builder)` comes last in `OnModelCreating`
   - unique indexes start with `tenant_id`
   - every new table gets RLS in its migration
   - never set `TenantId`
   - never call the parameterless `IgnoreQueryFilters()`
5. Every endpoint declares its authorization explicitly (an architecture test enforces it; undeclared Carter endpoints
   fall back to "authenticated user" as a safety net).
6. Never set `Version` by hand. Critical updates use `long? Version` + `dbContext.ExpectVersion(...)`.
7. No real credentials, hostnames or IPs in committed files. Use `CHANGE_ME`, `api.example.com`, `YOUR_SERVER_IP`.
8. No new markdown in the repo root. Docs go in `wiki/` (index: `wiki/INDEX.md`).

## Migrations (from the repo root)

```bash
dotnet ef migrations add <Name> --context <ModuleDbContext> \
  --project src/Modules/<Module>/<Module> --startup-project src/Api --output-dir Data/Migrations
```

## Step-by-step procedures

Each recurring change (module, feature slice, aggregate, migration, integration event, job, permission, email
template, tests, review, new project, docs) has a checklist with templates in
`tools/claude/starterkit-plugin/skills/<name>/SKILL.md`. Agents that can't load Claude Code plugins should read the
matching `SKILL.md` and follow it.
