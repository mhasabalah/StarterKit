# PostgreSQL image with pgBackRest

`docker-compose.yml` builds this image for two services:

| Service | Runs | Purpose |
|---|---|---|
| `postgres` | `postgres -c ...` | The database. Its `archive_command` (`pgbackrest-archive-wal %p`) pushes WAL with pgBackRest. |
| `pgbackrest` | `tail -f /dev/null` | A long-running backup host: `docker compose exec -u postgres pgbackrest pgbackrest ...` takes backups, `docker compose run --rm pgbackrest pgbackrest ... restore` restores. |

Both use the same pgBackRest version and the same rendered configuration, and run pgBackRest as the `postgres`
user that owns the data directory, so a restored cluster has the ownership PostgreSQL expects.

## Files

- `Dockerfile`: `postgres:16-alpine` plus `pgbackrest` (pinned by `PGBACKREST_VERSION`) and `su-exec`.
- `pgbackrest.conf.template`: the configuration, baked into the image. Rebuild after editing it
  (`docker compose build postgres pgbackrest`).
- `scripts/entrypoint.sh`: renders the configuration, then starts PostgreSQL through the official entrypoint, or
  runs any other command as `postgres`.
- `scripts/render-pgbackrest-conf.sh`: fills the template placeholders from the environment and writes
  `/etc/pgbackrest/pgbackrest.conf` with mode 0640 (it contains the S3 credentials).
- `scripts/archive-wal.sh`: the `archive_command`.

## Placeholders

| Placeholder | Environment variable (`.env.prod`) |
|---|---|
| `{{S3_BACKUP_BUCKET}}` | `S3_BACKUP_BUCKET` |
| `{{AWS_DEFAULT_REGION}}` | `AWS_DEFAULT_REGION` (default `us-east-1`) |
| `{{AWS_ACCESS_KEY_ID}}` | `AWS_ACCESS_KEY_ID` |
| `{{AWS_SECRET_ACCESS_KEY}}` | `AWS_SECRET_ACCESS_KEY` |
| `{{POSTGRES_USER}}` | `POSTGRES_USER` |
| `{{POSTGRES_DB}}` | `POSTGRES_DB` |

pgBackRest connects through the shared Unix socket (`/var/run/postgresql`), so the database password is not in the
configuration.

## WAL archiving

- `S3_BACKUP_BUCKET` empty (or still the `.env.example` placeholder): there is no repository, so `pgbackrest-archive-wal`
  reports each segment as archived and PostgreSQL recycles it. The data volume cannot fill up with unarchived WAL.
- `S3_BACKUP_BUCKET` set: every segment is pushed with `pgbackrest archive-push`; a failure makes PostgreSQL keep
  the segment and retry. `archive-push-queue-max` (1 GiB) bounds how much WAL accumulates while S3 is unreachable.
  Create the stanza right after the first deploy (`deployment/postgres/scripts/init-pgbackrest.sh`), otherwise
  pushes fail until it exists.
