# Testing the production setup locally

Run the **production** compose stack on your machine before a release, or after changing `docker-compose.yml`,
Nginx or secrets handling. Dev overrides are excluded, because `-f docker-compose.yml` disables the automatic merge
of `docker-compose.override.yml`.

## Goal

Exercise the selected production image/configuration locally before a release, including migration failure,
authentication, readiness and operational access.

## Prerequisites

Have Docker/Compose 2.24+, a built image, an isolated set of volumes/ports and local test credentials. Do not
point this rehearsal at production data. S3 access is needed only for an actual backup rehearsal. See
[Compose](Docker-Compose.md) for overlay coverage and [Secrets](Secrets.md) for required file mounts.

## Steps

1. Prepare the environment and image.

### Prepare

```bash
cp deployment/.env.production.example .env.prod      # replace every CHANGE_ME (local values are fine)
./deployment/secrets-setup.sh                        # creates secrets/*.txt
docker build -t ghcr.io/your-org/starterkit-api:local -f src/Api/Dockerfile .
# in .env.prod: DOCKER_IMAGE=ghcr.io/your-org/starterkit-api, DOCKER_IMAGE_TAG=local
```

2. Select the core or required overlays.

### Run

```bash
docker compose -f docker-compose.yml --env-file .env.prod up -d                                        # core
docker compose -f docker-compose.yml -f docker-compose.observability.yml --env-file .env.prod up -d    # + observability
docker compose -f docker-compose.yml -f docker-compose.rabbitmq.yml --env-file .env.prod up -d         # RabbitMQ transport
docker compose -f docker-compose.yml -f docker-compose.dataprotection.yml --env-file .env.prod up -d   # encrypted DP keys
```

## Verify

| Check | Expect |
|---|---|
| `docker compose -f docker-compose.yml --env-file .env.prod ps` | Long-lived services running; `migrator` exits 0 (use `ps -a`) |
| `curl -s http://localhost:${API_PORT:-5000}/health` | `Healthy` (PostgreSQL + Redis) |
| `curl -s http://localhost:${API_PORT:-5000}/alive` | `Healthy` |
| API log at startup | `[secrets] Loaded N secret(s)`, inspect **migrator** logs for migrations/seeds and exit 0, non-durable `InMemory` is rejected, not merely warned about |
| Register, log in and call an authorised endpoint | 200; anonymous calls return 401 |
| Seq (`SEQ_PORT`) | Structured logs with trace ids; first-login password set ([Seq-Security.md](Seq-Security.md)) |
| Backup | Inspect backup labels/status and rehearse a restore; see [wiring limitations](Backups.md#wiring-limits-to-resolve-before-production-use). |

To test Nginx and TLS locally, see [Nginx.md](Nginx.md). Use a self-signed certificate in place of the
Let's Encrypt paths in a *copy* of `deployment/nginx/conf.d/app-production.conf`. Never edit the original for
testing.

Verify the **resolved container environment**: the base file sets `ASPNETCORE_ENVIRONMENT: Docker` literally,
so adding a Production assignment to `.env.prod` does not switch it to Production. Docker settings disable startup migration
and Swagger. An enablement change needs a matching environment override, not merely a variable in an env file.

Exercise failed migration startup in isolation: the API must not start when migrator fails. Check an authorized
business operation, 401 for an anonymous protected call and 403 for insufficient permission. Confirm existing
Data Protection payloads survive a container replacement when the encryption overlay is selected. API `/alive`
can remain healthy during a dependency outage while `/health` reports unready.

## Pitfalls

Core and full telemetry ports are not automatically private. Dev settings can leak in if `-f` is omitted.
Use the same file selection for inspection and shutdown, and avoid misleading a running production instance by
reusing its project/volume names. Adding `.env.prod` values alone does not add services or secret mounts.

### Clean up

```bash
docker compose -f docker-compose.yml --env-file .env.prod down        # add -v to drop volumes (destroys data)
```

Don't commit `.env.prod` or `secrets/`; both are git-ignored.

## Related

[VPS deployment](Deploy-to-VPS.md), [migrations](Database-Migrations.md), [Seq security](Seq-Security.md),
[testing strategy](../Testing/Testing-Strategy.md), and `/starterkit:write-tests`.
