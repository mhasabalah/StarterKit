# StarterKit

A production-ready **ASP.NET Core 9 modular monolith starter template**. Clone it, rename it, delete the sample
module, and start building features on top of auth, files, subscriptions, notifications, observability and a
complete Docker / Nginx / GitHub Actions deployment pipeline.

## Features

- **Modular monolith + vertical slices** - each module owns its DbContext, schema, contracts and endpoints
- **Identity** - registration/login, JWT access + rotating refresh tokens, token blacklist (Redis), roles (User/Admin/SuperAdmin), admin & user management, phone/email verification, password reset, external login hooks
- **Attachment** - uploads with validation, thumbnails, pending (anonymous) upload sessions, Local / Azure Blob / AWS S3 storage
- **Subscriptions** - subscribers, plans, subscriptions, payment methods and instructions
- **Catalog (sample)** - a minimal Product aggregate showing the full CRUD slice: validator + handler, Carter endpoint, repository, projection query, optimistic concurrency, tests
- **Shared** - CQRS abstractions, pipeline behaviors (validation, logging, concurrency → 409), pagination, enum lookup, Email (SMTP + logs), Notifications (WhatsApp/Email)
- **Infrastructure** - PostgreSQL (EF Core, snake_case), Redis, MassTransit, Hangfire, rate limiting, CORS, Serilog + Seq, OpenTelemetry (Prometheus, Grafana, Tempo, Loki, Jaeger)
- **Ops** - .NET Aspire AppHost, Docker Compose (dev override + observability), Nginx configs, Docker secrets, pgBackRest backups, zero-downtime deploy + rollback scripts, GitHub Actions CI/CD to GHCR + self-hosted runner

## Tech Stack

.NET 9 · ASP.NET Core minimal APIs (Carter) · MediatR · FluentValidation · Mapster · EF Core 9 + Npgsql · PostgreSQL 16 · Redis 7 · MassTransit · Hangfire · Serilog/Seq · OpenTelemetry · .NET Aspire · Docker · Nginx · GitHub Actions

## Quick Start

### Option 1 - .NET Aspire (recommended for development)

Requires the .NET 9 SDK and Docker.

```bash
cd src/Aspire/AppHost
dotnet run
```

The Aspire dashboard starts PostgreSQL (database `starterkit`), Redis, Seq and the observability containers, then runs the API.

### Option 2 - Docker Compose

```bash
cp .env.example .env          # then replace the CHANGE_ME values
docker compose up -d          # docker-compose.override.yml (dev tools) is merged automatically
```

| Service | URL |
|---------|-----|
| API + Swagger | http://localhost:5000/swagger |
| Seq | http://localhost:5341 |
| PgAdmin | http://localhost:5050 |
| Redis Commander | http://localhost:8081 |
| Mailhog | http://localhost:8025 |

(Ports are configurable in `.env`.)

Production-style runs:

```bash
docker compose -f docker-compose.yml up -d                                          # core only (needs ./secrets)
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d     # + observability
```

### Build and test

```bash
dotnet build StarterKit.slnx
dotnet test StarterKit.slnx
```

## Project Structure

```
src/
├── Api/                      # Host: Program.cs, appsettings, Dockerfile
├── Api.E2ETests/
├── Aspire/                   # AppHost + ServiceDefaults
├── Modules/
│   ├── Identity/             # Identity + Identity.Contracts
│   ├── Attachment/           # Attachment + Attachment.Contracts
│   ├── Subscriptions/        # Subscriptions + .Contracts + .Tests
│   └── Catalog/              # Sample module: Catalog + .Contracts + .Tests
└── Shared/                   # Shared.Contracts, Shared.Core, Shared.Messaging, Shared.Email, Shared.Notification
tests/                        # Shared / e2e tests
infra/                        # Nginx, observability, pgAdmin configs
deployment/                   # deploy / rollback / backup scripts, production Nginx, .env.production.example
wiki/                         # Developer documentation
```

## Starting a new project

1. Copy the repository, rename `StarterKit.slnx`, and search/replace `starterkit` / `StarterKit`.
2. Fill in `.env` and `secrets/` (see `secrets/README.md`) - every `CHANGE_ME` must be replaced.
3. Remove the Catalog sample when you no longer need it, and regenerate migrations if you change the schema.
4. Configure GitHub environment, secrets and repository variables for the CI/CD workflow.

The detailed checklist is in [CLAUDE.md](CLAUDE.md#starting-a-new-project-from-starterkit); adding a module is covered in [wiki/Modules/Catalog.md](wiki/Modules/Catalog.md).

## Documentation

- [wiki/INDEX.md](wiki/INDEX.md) - all developer docs (architecture, modules, deployment)
- [CLAUDE.md](CLAUDE.md) - conventions, patterns and rules (also used by Claude Code)
- [AGENTS.md](AGENTS.md) - the same guidance for other AI coding agents

## Security

No real credentials are committed. `appsettings*.json`, `.env.example` and `deployment/.env.production.example` contain
placeholders only; production secrets are supplied through Docker secrets (`./secrets/*.txt`) or environment variables.
