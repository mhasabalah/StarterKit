# pgBackRest Configuration

This directory contains the pgBackRest configuration template for the woblerr/pgbackrest Docker image.

## Files

- **pgbackrest.conf.template** - Configuration template with placeholders for environment variables
- **README.md** - This file

## How It Works

**woblerr/pgbackrest** requires a configuration file and does NOT support `PGBACKREST_*` environment variables.

Since we can't hardcode secrets in the config file, we use a **template approach**:

1. **Template file** (`pgbackrest.conf.template`) contains placeholders like `{{AWS_ACCESS_KEY_ID}}`
2. **Entrypoint script** replaces placeholders with environment variable values
3. **Generated config** is created at `/etc/pgbackrest/pgbackrest.conf` in container

### Template Placeholders

The template uses these placeholders (replaced at runtime):

- `{{S3_BACKUP_BUCKET}}` → From `.env.prod`
- `{{AWS_ACCESS_KEY_ID}}` → From `.env.prod`
- `{{AWS_SECRET_ACCESS_KEY}}` → From `.env.prod`
- `{{AWS_DEFAULT_REGION}}` → From `.env.prod`
- `{{POSTGRES_USER}}` → From `.env.prod`
- `{{POSTGRES_DB}}` → From `.env.prod`

### PostgreSQL Password

PostgreSQL password is NOT in the config file. It's handled via `.pgpass`:

- Created by entrypoint script from Docker secret
- Location: `/home/pgbackrest/.pgpass`
- Format: `postgres:5432:*:starterkit_user:password`

## Mounted in docker-compose.yml

```yaml
volumes:
  # Template is mounted (read-only)
  - ./deployment/postgres/config/pgbackrest.conf.template:/tmp/pgbackrest.conf.template:ro

environment:
  # Variables for template substitution
  - S3_BACKUP_BUCKET=${S3_BACKUP_BUCKET}
  - AWS_ACCESS_KEY_ID=${AWS_ACCESS_KEY_ID}
  - AWS_SECRET_ACCESS_KEY=${AWS_SECRET_ACCESS_KEY}
  - POSTGRES_USER=${POSTGRES_USER}
  - POSTGRES_DB=${POSTGRES_DB}
```

## Editing Configuration

To change pgBackRest settings:

1. Edit `pgbackrest.conf.template`
2. Restart pgbackrest service:
   ```bash
   docker compose --env-file .env.prod restart pgbackrest
   ```

The entrypoint script will regenerate `/etc/pgbackrest/pgbackrest.conf` with updated values.

## Security

✅ **Secrets are NOT in Git:**
- Template file is safe to commit (has placeholders, not actual secrets)
- Actual secrets come from `.env.prod` (not in Git)
- Generated config file exists only inside container

## Generated Config Format

The entrypoint generates a config file like:

```ini
[global]
repo1-type=s3
repo1-s3-bucket=starterkit-demo
repo1-s3-endpoint=s3.us-east-1.amazonaws.com
repo1-s3-key=YOUR_AWS_ACCESS_KEY_ID
repo1-s3-key-secret=YOUR_S3_SECRET_KEY
repo1-s3-region=us-east-1
# ... etc

[starterkit]
pg1-host=postgres
pg1-user=starterkit_user
pg1-database=starterkit
# ... etc
```

## Reference

- pgBackRest Configuration: https://pgbackrest.org/configuration.html
- woblerr/pgbackrest: https://github.com/woblerr/docker-pgbackrest
