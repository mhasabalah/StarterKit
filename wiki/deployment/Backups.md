# Database backups and restore (pgBackRest)

The `pgbackrest` service and scheduled workflow define PostgreSQL-to-S3 backups, with deployment wiring that must
be validated before relying on recovery.

## Goal

Produce inspectable off-host backups and rehearse recovery without confusing a logical pre-deploy dump with a
physical cluster backup or a complete backup of application secrets and attachments.

## Prerequisites

Configure S3 access, bucket/region and the selected database credentials. Read the wiring limits below before
relying on the helper scripts. Work from the deployment checkout; schedule and restore examples assume the
default `starterkit` stanza.

## Steps

1. Review the configuration, image and WAL path before initializing the stanza.
2. Initialize and take a full backup, then inspect it before enabling a schedule.
3. Monitor backup age and rehearse restoration on an isolated server.

### How it is wired

- **Configuration:** `deployment/postgres/config/pgbackrest.conf.template` is rendered at container start.
  - Placeholders (`{{S3_BACKUP_BUCKET}}`, `{{AWS_*}}`, `{{POSTGRES_USER}}`, `{{POSTGRES_DB}}`) come from `.env.prod`.
  - The PostgreSQL password comes from the `postgres_password` Docker secret via `.pgpass`; it is never in the config
    file.
<!--#if (deploy)-->
  - See [`deployment/postgres/config/README.md`](../../deployment/postgres/config/README.md).
<!--#endif-->
- **Repository:** `repo1-type=s3`, path `/pgbackrest`, TLS verified.
- **Retention and compression:** set in the template (4 full and 8 differential backups by default, LZ4
  compression).
- **Stanza:** the template hard-codes `[starterkit]`; workflows use `APP_NAME` (default `starterkit`). Keep those aligned. The initialization helper is
  `deployment/postgres/scripts/init-pgbackrest.sh`, intended to create the stanza, check configuration and take a first full backup.
- **Scripts:** helper scripts are in `deployment/postgres/scripts/`: `backup-full|diff|incr|info|health.sh`,
  `restore.sh`, `restore-verbose.sh` and `backup-cron.sh`.

### Schedule

`.github/workflows/database-backup.yml` runs on the self-hosted runner. It sends optional Telegram notifications.

| When | Type |
|---|---|
| Sunday 02:00 UTC | Full |
| Monday-Saturday 02:00 UTC | Differential |
| Manual (`workflow_dispatch`) | Any |

If you don't use GitHub Actions, the cron alternative is:

```bash
0 2 * * 0    cd /opt/starterkit && ./deployment/postgres/scripts/backup-full.sh   >> /var/log/pgbackrest/cron.log 2>&1
0 2 * * 1-6  cd /opt/starterkit && ./deployment/postgres/scripts/backup-diff.sh   >> /var/log/pgbackrest/cron.log 2>&1
*/30 * * * * cd /opt/starterkit && ./deployment/postgres/scripts/backup-health.sh >> /var/log/pgbackrest/health.log 2>&1
```

`deployment/pre-deploy-backup.sh` is a separate helper. The hybrid deploy workflow takes a **logical** `pg_dump` into `/backup` before replacing an existing deployment; it does not take a pgBackRest physical backup at that step.

### Manual backup and inspection

```bash
cd /opt/starterkit      # DEPLOY_DIR
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=full --no-archive-check backup       # or diff / incr

docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest pgbackrest --stanza=starterkit info
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest pgbackrest --stanza=starterkit verify
```

### Restore

The helper exposes these recovery modes (review the limitations below before executing; restore overwrites the database):

```bash
./deployment/postgres/scripts/restore.sh latest                         # latest backup
./deployment/postgres/scripts/restore.sh time "2026-02-07 14:30:00"     # point-in-time recovery
./deployment/postgres/scripts/restore.sh immediate                      # last consistent state
```

The existing recovery commands below describe the intended sequence, **not a verified copy-paste runbook**.
The pgBackRest `run` command conflicts with the service entrypoint; resolve the
[wiring limits](#wiring-limits-to-resolve-before-production-use) and rehearse on an isolated server before use:

1. `docker compose -f docker-compose.yml --env-file .env.prod stop api postgres`
2. Clear the data directory:
   `docker compose -f docker-compose.yml --env-file .env.prod run --rm postgres sh -c "rm -rf /var/lib/postgresql/data/pgdata/*"`
3. Restore:
   `docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest pgbackrest --stanza=starterkit [--set=<label>] --delta restore`
4. `docker compose -f docker-compose.yml --env-file .env.prod start postgres`, then check it with
   `psql -c "\dt"`.
5. `docker compose -f docker-compose.yml --env-file .env.prod run --rm migrator` to apply any newer migrations (the
   production API does not migrate on startup), then
   `docker compose -f docker-compose.yml --env-file .env.prod start api`.

The helper currently clears the data directory before validating the requested recovery type, and restarts API
without an explicit migrate-only step. It also uses the commands discussed in the entrypoint limitation below.
Do not use it unchanged as a production recovery procedure. Validate the target, backup label and recovery time
first, stop all writers (including optional workers), restore in isolation and apply compatible migrations before
resuming traffic. Practise recovery at least quarterly and record the measured recovery time and data-loss window.

## Verify

Use `info` to inspect backup labels, age and status and `verify` to check backup contents. A workflow step named
Verify backup currently runs `info`; it is not a restore rehearsal. Check archived WAL and rehearse the required
point-in-time target as well as latest recovery. Verify application data and tenant isolation after restoration;
a public-schema table count alone is insufficient for module-owned schemas.

### Wiring limits to resolve before production use

- The base PostgreSQL service uses `postgres:16-alpine`, but `archive_command` invokes `pgbackrest`. The base
  file does not install that binary/configuration in the PostgreSQL image; the separate backup sidecar does not
  make the command available in PostgreSQL. Verify archive delivery in the actual deployed image.
- The `pgbackrest` service overrides entrypoint/command with a config-rendering shell and `tail -f /dev/null`.
  `exec pgbackrest pgbackrest ...` targets a running sidecar; `run --rm pgbackrest pgbackrest ...` replaces its
  shell command and is not equivalent with that entrypoint. The restore helpers use the latter form. Reconcile
  this before running the documented manual restore sequence.
- Several backup commands use `--no-archive-check`. A successful backup under that flag does not establish
  healthy WAL archiving or point-in-time recovery.
- Physical backups cover the configured PostgreSQL cluster. Inventory every dedicated tenant database/server;
  a `pg_dump` of the shared database does not cover them. Back up blob storage, secrets and Data Protection
  keys/certificates separately.

## Pitfalls

### Troubleshooting

| Symptom | Check |
|---|---|
| Backup fails at start | Validate rendered configuration inside the container without copying it to logs: it contains S3 credentials. |
| Cannot connect to PostgreSQL | `psql -U <POSTGRES_USER> -d <POSTGRES_DB> -h /var/run/postgresql -c "select 1"` inside the `pgbackrest` container (shared socket volume) |
| S3 errors | Bucket name and region in `.env.prod`; IAM permissions for `s3:ListBucket`, `GetObject`, `PutObject` and `DeleteObject` on `arn:aws:s3:::<bucket>/pgbackrest/*` |
| Archive check fails | The `archive_command` and WAL volume on the `postgres` service; `pgbackrest --stanza=<name> check` |
| Logs | `docker compose -f docker-compose.yml --env-file .env.prod logs pgbackrest` and `/var/log/pgbackrest/` in the container |

### Security

- The config is generated at runtime, but contains S3 credentials and is written mode 644 by the current shell; restrict container/host access and review permissions.
- Encryption at rest (`repo1-cipher-type`) is off by default. Enable it for regulated data, and store the passphrase
  as a secret.
- Restrict the bucket with a dedicated IAM principal and block public access.

Reference: <https://pgbackrest.org/user-guide.html>

## Related

[CI/CD rollback](CI-CD.md#rollback-behavior), [migrations](Database-Migrations.md), [secrets](Secrets.md),
[dedicated databases](../MultiTenancy/Isolation-and-Dedicated-Databases.md), and `/starterkit:add-migration`.
