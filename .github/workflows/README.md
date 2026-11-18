# GitHub Actions workflows

The full documentation, including triggers, jobs, the GitHub environment, repository variables and secrets, is
**[wiki/deployment/CI-CD.md](../../wiki/deployment/CI-CD.md)**.

| Workflow | Purpose |
|---|---|
| `hybrid-ci-cd.yml` | Build and test on PRs and pushes; the GHCR image and the deploy only from Run workflow (manual approval, self-hosted runner) |
| `quality.yml` | Docs check, C# whitespace ratchet, plugin manifests (one job): on every push and PR |
| `clamav-live.yml` | Real ClamAV image test (`ClamAvContainerTests`): nightly, on PRs touching the scanner, on demand |
| `rollback.yml` | Manual production rollback (type `ROLLBACK`) |
| `database-backup.yml` | pgBackRest full (Sun) and differential (Mon–Sat) backups |
| `health-monitoring.yml` | Health and resource checks every 6 h (disable it where nothing is deployed) |
| `test-runner.yml` | Self-hosted runner smoke test |
| `claude.yml` | Claude Code assistant on `@claude` mentions (owner, members and collaborators only) |
| `claude-code-review.yml` | Claude Code PR review: **manual only** (Run workflow with a PR number) |

Related: [Self-Hosted Runner](../../wiki/deployment/Self-Hosted-Runner.md) ·
[Deploy to VPS](../../wiki/deployment/Deploy-to-VPS.md) · [Backups](../../wiki/deployment/Backups.md)
