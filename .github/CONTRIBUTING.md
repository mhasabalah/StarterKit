# Contributing to StarterKit

Thanks for helping. This page is the short version; the rules live in [`CLAUDE.md`](../CLAUDE.md), and the
explanations in [`wiki/`](../wiki/README.md).

## Setup

1. Install the .NET SDK from `global.json` (10.0.x) and Docker, then run `dotnet tool restore` (installs `dotnet ef`
   at the version pinned in `.config/dotnet-tools.json`).
2. `cd src/Aspire/AppHost && dotnet run` starts the API, Postgres, Redis and Seq. Alternatively, run
   `cp .env.example .env && docker compose up -d`.
3. `dotnet build StarterKit.slnx`, then `dotnet test StarterKit.slnx` (integration and E2E tests need Docker).

## Workflow

1. Branch from `master`: `feature/<short-name>`, `fix/<short-name>` or `docs/<short-name>`.
2. Find the right procedure:
   - `wiki/INDEX.md` → *By task*
   - with Claude Code, the matching `/starterkit:*` skill (`new-module`, `add-feature`, `add-aggregate`,
     `add-migration`, `add-integration-event`, `add-background-job`, `add-permission`, `add-email-template`,
     `write-tests`)
3. Keep each PR to one concern. Refactors and behaviour changes go in separate PRs.
4. Before pushing:

   ```bash
   dotnet build StarterKit.slnx
   dotnet test src/Api.Tests                                 # architecture guardrails (fast)
   dotnet test <the projects you touched>
   dotnet format whitespace . --folder --include <changed .cs files>
   bash scripts/check-docs.sh                                # if you touched docs
   ```

5. Open the PR and fill in the template checklist. CI runs **Quality** (docs, format ratchet, plugin manifests) and
   **Hybrid CI/CD** (build, migrations check, all tests).

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat(catalog): ...`, `fix(identity): ...`,
`docs: ...`, `test(api): ...`, `build(deploy): ...`, `refactor: ...`. Write in the imperative and explain *why* in
the body.

## Review standards

Reviewers follow the `/starterkit:review` checklist:
- **Architecture:** module boundaries, tenant isolation, outbox, explicit auth.
- **Code quality:** DRY, explicit over clever.
- **Tests:** levels and failure modes.
- **Performance:** N+1 queries, projections, unbounded queries.

Changing a rule in `CLAUDE.md` or an architecture test requires an ADR in [`wiki/Decisions/`](../wiki/Decisions/README.md).

## Security

Never commit secrets, real hostnames or IPs. Report vulnerabilities privately; see [SECURITY.md](SECURITY.md).
