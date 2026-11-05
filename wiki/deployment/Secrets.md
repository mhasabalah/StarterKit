# Secrets

How StarterKit gets secrets in each environment. **No real secret is ever committed.** `appsettings*.json`,
`.env.example` and `deployment/.env.production.example` contain placeholders (`CHANGE_ME`) only.

| Environment | Source |
|---|---|
| Development (Aspire / `dotnet run`) | `appsettings.Development.json` defaults, `dotnet user-secrets`, environment variables. Docker secrets are skipped when `ASPNETCORE_ENVIRONMENT=Development`. |
| Docker Compose / production | Docker secrets: files in `./secrets/*.txt` (git-ignored), mounted at `/run/secrets/<name>` |
| CI (GitHub Actions) | Repository **secrets** (sensitive) and **variables** (non-sensitive). See [CI-CD.md](CI-CD.md#github-setup). |

## Docker secrets

`src/Api/Infrastructure/DockerSecretsConfigurationProvider.cs` (`builder.Configuration.AddDockerSecrets()`, the first
line of `Program.cs`) loads every file in `/run/secrets` into configuration:

- **Known short names** map to configuration keys:

  | Secret file | Configuration key(s) |
  |---|---|
  | `jwt_secret` | `JWT_SECRET`, `JwtSettings:SecretKey` (warns if shorter than 32 chars) |
  | `postgres_password` | `POSTGRES_PASSWORD`, also builds `ConnectionStrings:starterkit` from `POSTGRES_HOST/PORT/DB/USER` |
  | `redis_password` | `REDIS_PASSWORD`, also builds `ConnectionStrings:Redis` from `REDIS_HOST/PORT` |
  | `identity_otp_hmac_key` | `Identity:OtpHmacKey` |
  | `hangfire_dashboard_password` | `Hangfire:Dashboard:Password` |
  | `smtp_password` | `Email:SmtpPassword` |
  | `rabbitmq_password` | `Messaging:RabbitMq:Password` |
  | `dataprotection_cert_password` | `Identity:DataProtection:Certificate:Password` |

- **Migration connection string.** Compose maps the secret `migration_connection_string`
  (`secrets/migration_connection_string.txt`, may be empty) into the migrator container as
  `/run/secrets/Database__MigrationConnectionString`, which becomes `Database:MigrationConnectionString`. It is a
  secret, not an env var, so it does not show in `docker inspect`.
- **Any other file** becomes a key from its name, with `__` turned into `:`. For example, `Storage__S3__SecretKey`
  maps to `Storage:S3:SecretKey`.
- **Binary and key files** (`.pfx`, `.p12`, `.json`) are not loaded as values. They are referenced by path, for
  example `Identity:DataProtection:Certificate:Path=/run/secrets/dataprotection_cert.pfx` or the FCM service-account
  key `Push:CredentialsFile=/run/secrets/fcm_service_account.json` (`docker-compose.push.yml`): a private key never
  enters configuration.

## Creating the secret files

The file list and the commands are in [`secrets/README.md`](../../secrets/README.md). The quickest path is the
helper script:

```bash
./deployment/secrets-setup.sh          # creates any missing secrets/*.txt with strong random values
chmod 600 secrets/*
```

Rules:
- Generate values with `openssl rand -base64 32` (48 for the JWT key), with no trailing newline.
- **Don't rotate `dataprotection_cert.pfx` casually.** It encrypts the Data Protection key ring, and replacing it
  invalidates existing protected payloads such as auth cookies and antiforgery tokens.
- Rotate `jwt_secret` only when you accept that every existing access and refresh token becomes invalid.
- Back up `secrets/` securely (password manager or vault). It is not in git and not in the database backups.

## Non-secret production settings

`.env.prod` on the server (from `deployment/.env.production.example`) holds non-secret settings:

- the domain, image and tag
- Postgres user and DB names
- S3 bucket and region for backups
- the messaging transport

AWS keys for pgBackRest also live there today. Prefer an instance role or IAM user scoped to the backup bucket.

## Checklist

- [ ] Every `CHANGE_ME` in `.env.prod` is replaced.
- [ ] `secrets/*.txt` exist, are `chmod 600`, and are not tracked (`git status` is clean).
- [ ] The API log at startup shows `[secrets] Loaded N secret(s): ...` with the expected names.
- [ ] Seq, Grafana and the Hangfire dashboard use non-default passwords (see [Seq-Security.md](Seq-Security.md)).
