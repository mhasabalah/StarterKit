# CI/CD (GitHub Actions)

All workflows are in `.github/workflows/`. Deployment targets one VPS that runs Docker Compose behind host Nginx,
through a **self-hosted runner** labelled `deploy-only` ([Self-Hosted-Runner.md](Self-Hosted-Runner.md)).

## Workflows

| Workflow | Trigger | Runs on | What it does |
|---|---|---|---|
| `hybrid-ci-cd.yml` (Hybrid CI/CD) | push to `master`/`develop` (markdown/wiki changes ignored), PR to `master`, manual | GitHub-hosted, then self-hosted | **build-and-test**: restore, build Release, `PendingModelChangesTests`, `dotnet test StarterKit.slnx`. **build-and-push** (master/manual): image to `ghcr.io/<owner>/<repo>-api`. **deploy** (`production` environment, so it **waits for manual approval**): GHCR login, DB backup, pull, zero-downtime `api` replacement, supporting services, cleanup, health verification, Telegram notice. |
| `quality.yml` (Quality) | every push to `master`/`develop`, every PR (docs-only changes included), manual | GitHub-hosted | **docs**: `scripts/check-docs.sh`. **format**: `dotnet format whitespace --verify-no-changes` on the C# files the change touches (a ratchet; legacy files converge as they are edited). **plugin**: Claude Code plugin manifests, skills frontmatter and hook scripts parse |
| `rollback.yml` | manual (type `ROLLBACK`, optional backup file) | self-hosted | Restores the previous image and, if asked, a database backup |
| `database-backup.yml` | Sun 02:00 full, Mon-Sat 02:00 diff, manual | self-hosted | pgBackRest backup + verification + metrics ([Backups.md](Backups.md)) |
| `health-monitoring.yml` | every 6 h, manual | hosted + self-hosted | Public `/health` checks, plus container and disk resource checks on the VPS |
| `test-runner.yml` | manual | self-hosted | Smoke-tests the runner: basic, Docker, deployment dry-run |
| `claude.yml`, `claude-code-review.yml` | issue/PR comments, PRs | GitHub-hosted | Claude Code assistant and automatic PR review (`CLAUDE_CODE_OAUTH_TOKEN`) |

The deploy job uses `docker compose -f docker-compose.yml --env-file .env.prod` in `DEPLOY_DIR`. It only replaces
the `api` container (`up -d --no-deps api`) when Postgres is already up. It verifies `http://localhost:5000/health`,
`/alive` and `${PUBLIC_API_URL}/health`.

## GitHub setup

1. **Environment:** Settings → Environments → `production`, with required reviewers. This is the manual approval
   gate before deploy.
2. **Repository variables** (Settings → Secrets and variables → Actions → *Variables*). Every workflow reads them
   with safe defaults.

   | Variable | Default | Meaning |
   |---|---|---|
   | `APP_NAME` | `starterkit` | Compose container prefix and pgBackRest stanza |
   | `DEPLOY_DIR` | `/opt/starterkit` | App directory on the VPS (holds `docker-compose.yml`, `.env.prod`, `secrets/`) |
   | `PUBLIC_API_URL` | `https://api.example.com` | Public base URL behind Nginx |
   | `POSTGRES_USER` | `starterkit_user` | DB user |
   | `POSTGRES_DB` | `starterkit` | DB name |
   | `BACKUP_BUCKET` | `your-backup-bucket` | S3 bucket for backups |

3. **Repository secrets** (*Secrets* tab):

   | Secret | Required | Used by |
   |---|---|---|
   | `GITHUB_TOKEN` | automatic | GHCR push/pull |
   | `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` | optional | Deploy, backup and health notifications |
   | `SIXLABORS_LICENSE_KEY` | optional | ImageSharp license (thumbnails) at build time |
   | `CLAUDE_CODE_OAUTH_TOKEN` | optional | Claude workflows |

   Application secrets (DB, Redis, JWT, ...) do **not** live in GitHub. They are files in `DEPLOY_DIR/secrets/` on
   the server ([Secrets.md](Secrets.md)).
4. **Runner:** register the VPS runner with the `deploy-only` label ([Self-Hosted-Runner.md](Self-Hosted-Runner.md)).
5. **GHCR:** the package `ghcr.io/<owner>/<repo>-api` must be readable by the runner. It is the same repo, so
   `GITHUB_TOKEN` works.

## Release flow

```
feature branch ──PR──▶ master ──push──▶ build-and-test ──▶ build-and-push (GHCR :latest + :sha)
                                                     └──▶ deploy (waits for approval) ──▶ health checks ──▶ notify
```

- PRs and `develop` run **build-and-test only**.
- To roll back, run **Actions → Rollback Production Deployment** and type `ROLLBACK`. Locally you can run
  `deployment/rollback.sh`.
- A failing `PendingModelChangesTests` means someone changed the EF model without a migration. Add one with
  `/starterkit:add-migration`.

## Adding a step

- Keep secrets in GitHub secrets or server files. Never echo them.
- Use the shared `env:` block (vars with defaults) rather than hard-coding names or paths. The template must work
  after the rename described in [New-Project](../Getting-Started/New-Project.md).
- Anything that touches the server goes in a job on `[self-hosted, deploy-only]`.
