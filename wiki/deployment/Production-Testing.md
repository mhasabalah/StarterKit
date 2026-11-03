# Testing the production setup locally

Run the **production** compose stack on your machine before a release, or after changing `docker-compose.yml`,
Nginx or secrets handling. Dev overrides are excluded, because `-f docker-compose.yml` disables the automatic merge
of `docker-compose.override.yml`.

## 1. Prepare

```bash
cp deployment/.env.production.example .env.prod      # replace every CHANGE_ME (local values are fine)
./deployment/secrets-setup.sh                        # creates secrets/*.txt
docker build -t ghcr.io/your-org/starterkit-api:local -f src/Api/Dockerfile .
# in .env.prod: DOCKER_IMAGE=ghcr.io/your-org/starterkit-api, DOCKER_IMAGE_TAG=local, ASPNETCORE_ENVIRONMENT=Production
```

## 2. Run

```bash
docker compose -f docker-compose.yml --env-file .env.prod up -d                                        # core
docker compose -f docker-compose.yml -f docker-compose.observability.yml --env-file .env.prod up -d    # + observability
docker compose -f docker-compose.yml -f docker-compose.rabbitmq.yml --env-file .env.prod up -d         # RabbitMQ transport
docker compose -f docker-compose.yml -f docker-compose.dataprotection.yml --env-file .env.prod up -d   # encrypted DP keys
```

## 3. Verify

| Check | Expect |
|---|---|
| `docker compose -f docker-compose.yml --env-file .env.prod ps` | Every service `Up (healthy)` |
| `curl -s http://localhost:${API_PORT:-5000}/health` | `Healthy` (Postgres + Redis) |
| `curl -s http://localhost:${API_PORT:-5000}/alive` | `Healthy` |
| API log at startup | `[secrets] Loaded N secret(s)`, migrations applied for every module, no `InMemory` transport warning if you set `Messaging:Transport` |
| Register, log in and call an authorised endpoint | 200; anonymous calls return 401 |
| Seq (`SEQ_PORT`) | Structured logs with trace ids; first-login password set ([Seq-Security.md](Seq-Security.md)) |
| Backup | `docker compose ... exec pgbackrest pgbackrest --stanza=starterkit info` (needs S3 credentials) |

To test Nginx and TLS locally, see [Nginx.md](Nginx.md). Use a self-signed certificate in place of the
Let's Encrypt paths in a *copy* of `deployment/nginx/conf.d/app-production.conf`. Never edit the original for
testing.

## 4. Clean up

```bash
docker compose -f docker-compose.yml --env-file .env.prod down        # add -v to drop volumes (destroys data)
```

Don't commit `.env.prod` or `secrets/`; both are git-ignored.
