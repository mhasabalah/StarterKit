# pgBackRest Architecture - No Config Files Approach

## Design Philosophy

**Everything via Docker primitives - NO config files!**

### Configuration Methods

| Component | Method | Location |
|-----------|--------|----------|
| **PostgreSQL** | Command-line args | `docker-compose.yml` → `postgres` → `command` |
| **pgBackRest** | Environment variables | `docker-compose.yml` → `pgbackrest` → `environment` |
| **Secrets** | Docker secrets | `/run/secrets/` |

##Authentication

pgBackRest authenticates to PostgreSQL using `.pgpass` file:

```bash
# Entrypoint script creates:
/root/.pgpass
postgres:5432:*:starterkit_user:password_from_secret

# Permissions:
chmod 600 /root/.pgpass
```

## Benefits

✅ Single source of truth (docker-compose.yml + .env.prod)
✅ Variables expand properly
✅ No confusion about active config
✅ Docker-native
✅ Easy to maintain

## No Config Files!

All configuration is in `docker-compose.yml` and `.env.prod` - check git history if you need config file examples.
