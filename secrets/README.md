# secrets/

Production secrets consumed by `docker-compose.yml` as Docker secrets (mounted at `/run/secrets/<name>`).
Everything in this folder except this README is git-ignored. **Never commit real secret files.**

| File | Used by | Notes |
|------|---------|-------|
| `postgres_password.txt` | postgres, pgbackrest, api | Password for `POSTGRES_USER` |
| `redis_password.txt` | redis, api | Redis `requirepass` |
| `jwt_secret.txt` | api | HS256 signing key, at least 32 random characters |

Create them with `deployment/secrets-setup.sh`, or manually:

```bash
openssl rand -base64 32 | tr -d '\n' > secrets/postgres_password.txt
openssl rand -base64 32 | tr -d '\n' > secrets/redis_password.txt
openssl rand -base64 48 | tr -d '\n' > secrets/jwt_secret.txt
chmod 600 secrets/*.txt
```

See `wiki/deployment/README.secrets.md` for details.
