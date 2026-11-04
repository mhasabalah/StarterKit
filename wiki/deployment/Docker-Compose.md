# Docker Compose

StarterKit's compose setup is one **base file** plus small **overlays**. Only the files listed here exist; combine
them with repeated `-f` flags. Without `-f`, `docker compose` merges `docker-compose.yml` with
`docker-compose.override.yml` automatically, which gives you the dev setup.

| File | Role | Services |
|---|---|---|
| `docker-compose.yml` | **Base / production core** | `postgres`, `pgbackrest`, `redis`, `seq`, `api` (image from GHCR), Docker secrets, networks `backend`/`database` |
| `docker-compose.override.yml` | **Dev** (merged automatically) | Dev ports and settings for postgres/redis/seq/api (builds `src/Api/Dockerfile`), `pgadmin`, `redis-commander`, `mailhog` |
| `docker-compose.observability.yml` | Observability overlay | `otel-collector`, `prometheus`, `grafana`, `tempo`, `loki`, exporters (`node`, `cadvisor`, `postgres`, `redis`), `jaeger` (`--profile jaeger`) |
| `docker-compose.override.observability.yml` | Dev tweaks for the overlay | Dev ports; enables Jaeger without the profile |
| `docker-compose.rabbitmq.yml` | Messaging overlay | `rabbitmq` + switches the API to `Messaging:Transport=RabbitMq` (needs `secrets/rabbitmq_password.txt`) |
| `docker-compose.dataprotection.yml` | Security overlay (**recommended in production**) | Encrypts the Data Protection key ring with `secrets/dataprotection_cert.pfx`; the API refuses to start without it |

## Common commands

```bash
# Development (base + override, merged automatically). Fill .env from .env.example first.
docker compose up -d
docker compose logs -f api

# Development + observability
docker compose -f docker-compose.yml -f docker-compose.override.yml \
  -f docker-compose.observability.yml -f docker-compose.override.observability.yml up -d

# Production core (server: run in DEPLOY_DIR with .env.prod)
docker compose -f docker-compose.yml --env-file .env.prod up -d

# Production + recommended overlays
docker compose -f docker-compose.yml -f docker-compose.dataprotection.yml \
  -f docker-compose.observability.yml --env-file .env.prod up -d

# Durable messaging with RabbitMQ (alternative: Messaging:Transport=PostgreSql, no extra service)
docker compose -f docker-compose.yml -f docker-compose.rabbitmq.yml --env-file .env.prod up -d
```

`make help` lists shortcuts (`make dev`, `make logs-api`, `make db-backup`, `make health`, ...).

## Ports

Host ports come from `.env`; the compose fallback is used when a variable is unset. `.env.example` deliberately
offsets Postgres, Redis and Seq in development so they don't clash with locally installed services. Never use 8888
for the API: the OTel collector publishes it.

| Service | `.env.example` | Compose fallback |
|---|---|---|
| API (`/swagger`, `/scalar`, `/health`) | `API_PORT=5000` | 5000 |
| PostgreSQL | `POSTGRES_PORT=5433` | 5432 |
| Redis | `REDIS_PORT=6380` | 6379 |
| Seq | `SEQ_PORT=5342` | 5341 |
| pgAdmin | `PGADMIN_PORT=5050` | 5050 |
| Redis Commander | `REDIS_COMMANDER_PORT=8081` | 8081 |
| Mailhog UI / SMTP | `MAILHOG_UI_PORT=8025` / `MAILHOG_SMTP_PORT=1025` | 8025 / 1025 |
| Grafana / Prometheus / Jaeger | fixed: 3000 / 9090 / 16686 | |

In production, only Nginx (80/443) is public. Keep the database, Redis and Seq ports bound to localhost or closed in
the firewall.

## Configuration and secrets

- Non-secret settings come from `.env` (dev) or `.env.prod` (server). The templates are `.env.example` and
  `deployment/.env.production.example`.
- Secrets are Docker secrets from `./secrets/*.txt`, mounted at `/run/secrets/`. See [Secrets.md](Secrets.md).
- `MESSAGING_TRANSPORT` defaults to `PostgreSql`. `InMemory` is not durable and the API **refuses to boot** with it
  outside Development/Testing (`ProductionConfigurationGuard`). Use the RabbitMQ overlay for a broker.
- The base file has a one-shot `migrator` service (same image, `--migrate-only`); `api` waits for it with
  `service_completed_successfully`. See [Database-Migrations.md](Database-Migrations.md).
- The `api` healthcheck probes `/health` (readiness: Postgres + Redis). `/alive` is liveness only (process); use it
  for restart decisions, never for "ready to serve".

## Troubleshooting

| Problem | Fix |
|---|---|
| Port already in use | Change the `*_PORT` value in `.env` |
| API unhealthy at start | `docker compose logs api`, then check the connection string, migrations and missing secrets (`[secrets]` lines) |
| Postgres password mismatch after changing the secret | The volume keeps the old password. Change it in SQL, or drop the volume (destroys data). See [../Reference/Troubleshooting-PostgreSQL-Authentication.md](../Reference/Troubleshooting-PostgreSQL-Authentication.md). |
| Production run picks up dev settings | You forgot `-f docker-compose.yml` (without it the override is merged) |

Related: [Observability.md](Observability.md) · [Nginx.md](Nginx.md) · [Production-Testing.md](Production-Testing.md) ·
[Deploy-to-VPS.md](Deploy-to-VPS.md)
