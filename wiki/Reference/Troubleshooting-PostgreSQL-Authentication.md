# Troubleshoot PostgreSQL authentication

Repair a PostgreSQL credential mismatch while preserving the existing database and identifying which configuration source the API actually uses.

## Goal

Resolve `password authentication failed` without resetting a database that contains required data.

## Prerequisites

- Access to the Compose project and its `.env` and secret files.
- A database administrator connection for changing an existing role password.
- A verified backup before any destructive reset; see [Backups](../deployment/Backups.md).

## Steps

### Problem

The API rejects a database connection with `password authentication failed`. First distinguish authentication failure from an unreachable service, occupied port or missing HTTPS certificate.

### Root cause

PostgreSQL initialization credentials apply only when the data directory is first created. Restarting the container or changing an initialization secret does not change a role password inside an existing database. Base `docker-compose.yml` uses `POSTGRES_PASSWORD_FILE` from `./secrets/postgres_password.txt`; the automatically merged `docker-compose.override.yml` clears that variable and uses `POSTGRES_PASSWORD` from `.env` for development.

1. Inspect the active project and service state:

   ```bash
   docker compose ps
   docker compose logs postgres
   docker compose logs api
   ```

   Avoid posting resolved Compose output or connection strings containing secrets. `docker-compose.yml` derives container/volume names from `COMPOSE_PROJECT_NAME` (default `starterkit`); use service names rather than assuming fixed container names.

2. Identify the effective configuration sources. `POSTGRES_USER` / `POSTGRES_DB` in `.env` select initialization names; the password source depends on the active Compose files as described above. The API's `ConnectionStrings:starterkit` can be overridden by the environment, Aspire, or `AddDockerSecrets`. Development JSON has separate local credentials. Align the effective connection with the existing database role, not every checked-in example file. See [Secrets](../deployment/Secrets.md).

3. For retained data, connect as an authorized PostgreSQL administrator and change the existing role password interactively. The service command opens the client:

   ```bash
   docker compose exec postgres psql -U <database-admin-role> -d <database-name>
   ```

   In `psql`, use `\password <runtime-role>` to avoid putting a password in shell history. Update the corresponding secret/configuration and recreate the affected clients so their pooled connections use the new credential. See [Database migrations](../deployment/Database-Migrations.md) for distinct runtime, migrator and RLS-bypass roles; changing one role does not rotate them all.

4. Only for a **disposable** local database, reinitialize instead. Stop the project and inspect the exact volume before deleting it:

   ```bash
   docker compose down
   docker volume ls
   docker volume inspect <confirmed-postgres-data-volume>
   docker volume rm <confirmed-postgres-data-volume>
   docker compose up -d
   ```

   The Compose volume key is `postgres-data`; its default physical name is `starterkit_postgres_data`, with a project-name prefix override. Deleting it permanently deletes its database contents. Do not enumerate and delete all matching volumes or use a blanket `down -v` as a password repair. PostgreSQL WAL and backup volumes have separate lifecycles; use [Backups](../deployment/Backups.md) when recovery is needed.

## Verify

Test the same role/database and a TCP password-authenticated connection, supplying the password interactively:

```bash
docker compose exec postgres psql -h localhost -U <runtime-role> -d <database-name> -W -c "SELECT 1;"
docker compose exec postgres psql -U <database-admin-role> -d <database-name> -c "\dn"
docker compose logs api
```

A local socket connection alone may use a different authentication rule and does not prove that the API password works. Check API readiness at `/health`; then exercise a database-backed API operation. `SELECT NOW();` is also a basic connectivity check, not proof that migrations, runtime grants or tenant RLS are correct.

## Pitfalls

### Password configuration

Keep credentials in ignored environment/secret files. Committed connection examples use `CHANGE_ME`; never solve a mismatch by committing a working password. A successful container health check does not prove the runtime role can authenticate remotely.

### Volume management

List and inspect a specific volume before a disposable reset. Keep the database, WAL and backup volume roles distinct. The old global-volume deletion and fixed-delay reset script are unnecessary for credential rotation; use Compose health/status and explicit verification instead.

### Related issues

For `Could not find file '/https/aspnetapp.pfx'`, inspect the active Compose environment and certificate mounts rather than assuming a legacy HTTPS path. The current development override sets `ASPNETCORE_URLS` to `http://+:80`; see [Docker Compose](../deployment/Docker-Compose.md).

If the host port is already allocated, check `POSTGRES_PORT` and the conflicting service. Change the configured host port or stop that specific service. Do not stop every Docker container on the machine. Container-to-container connections use the Compose service/port and do not follow host-port remapping.

### Prevention

Record the credential source and rotation process, keep `.env` and secret files out of git, test from the actual client context after rotation, and take regular verified backups. Never expect a restart to reapply PostgreSQL initialization values.

## Related

[Configuration](Configuration.md), [Secrets](../deployment/Secrets.md), [Backups](../deployment/Backups.md), [Deploy-time migrations](../deployment/Database-Migrations.md), and `/starterkit:review`.
