# Database backups and restore (pgBackRest)

PostgreSQL is backed up by **pgBackRest** to S3, from the `pgbackrest` service in `docker-compose.yml`. Production
commands always use `-f docker-compose.yml --env-file .env.prod`, so dev overrides are never merged.

## How it is wired

- **Configuration:** `deployment/postgres/config/pgbackrest.conf.template` is rendered at container start.
  - Placeholders (`{{S3_BACKUP_BUCKET}}`, `{{AWS_*}}`, `{{POSTGRES_USER}}`, `{{POSTGRES_DB}}`) come from `.env.prod`.
  - The Postgres password comes from the `postgres_password` Docker secret via `.pgpass`; it is never in the config
    file.
  - See [`deployment/postgres/config/README.md`](../../deployment/postgres/config/README.md).
- **Repository:** `repo1-type=s3`, path `/pgbackrest`, TLS verified.
- **Retention and compression:** set in the template (4 full and 8 differential backups by default, LZ4
  compression).
- **Stanza:** the stanza name is `APP_NAME` (default `starterkit`). Initialise it once with
  `deployment/postgres/scripts/init-pgbackrest.sh`, which creates the stanza, checks the config and takes the first
  full backup.
- **Scripts:** helper scripts are in `deployment/postgres/scripts/`: `backup-full|diff|incr|info|health.sh`,
  `restore.sh`, `restore-verbose.sh` and `backup-cron.sh`.

## Schedule

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

`deployment/pre-deploy-backup.sh` and the deploy workflow also take a backup before every deployment.

## Manual backup and inspection

```bash
cd /opt/starterkit      # DEPLOY_DIR
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=full --no-archive-check backup       # or diff / incr

docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest pgbackrest --stanza=starterkit info
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest pgbackrest --stanza=starterkit verify
```

## Restore

The recommended path is the script:

```bash
./deployment/postgres/scripts/restore.sh latest                         # latest backup
./deployment/postgres/scripts/restore.sh time "2026-02-07 14:30:00"     # point-in-time recovery
./deployment/postgres/scripts/restore.sh immediate                      # last consistent state
```

To restore manually:

1. `docker compose -f docker-compose.yml --env-file .env.prod stop api postgres`
2. Clear the data directory:
   `docker compose -f docker-compose.yml --env-file .env.prod run --rm postgres sh -c "rm -rf /var/lib/postgresql/data/pgdata/*"`
3. Restore:
   `docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest pgbackrest --stanza=starterkit [--set=<label>] --delta restore`
4. `docker compose -f docker-compose.yml --env-file .env.prod start postgres`, then check it with
   `psql -c "\dt"`.
5. `docker compose -f docker-compose.yml --env-file .env.prod start api`. The API applies any newer migrations on
   startup.

Practise a restore into a scratch server at least once a quarter. A backup you have never restored is not a backup.

## Troubleshooting

| Symptom | Check |
|---|---|
| Backup fails at start | `cat /etc/pgbackrest/pgbackrest.conf` in the container: are the placeholders rendered? |
| Cannot connect to Postgres | `psql -U <POSTGRES_USER> -d <POSTGRES_DB> -h /var/run/postgresql -c "select 1"` inside the `pgbackrest` container (shared socket volume) |
| S3 errors | Bucket name and region in `.env.prod`; IAM permissions for `s3:ListBucket`, `GetObject`, `PutObject` and `DeleteObject` on `arn:aws:s3:::<bucket>/pgbackrest/*` |
| Archive check fails | The `archive_command` and WAL volume on the `postgres` service; `pgbackrest --stanza=<name> check` |
| Logs | `docker compose -f docker-compose.yml --env-file .env.prod logs pgbackrest` and `/var/log/pgbackrest/` in the container |

## Security

- The config is generated at runtime, so no credentials are in git.
- Encryption at rest (`repo1-cipher-type`) is off by default. Enable it for regulated data, and store the passphrase
  as a secret.
- Restrict the bucket with a dedicated IAM principal and block public access.

Reference: <https://pgbackrest.org/user-guide.html>
