# Secrets

Supply credentials through environment-specific configuration and Docker-secret mounts while keeping committed
configuration limited to defaults and placeholders such as `CHANGE_ME`.

| Environment | Source |
|---|---|
| Development (Aspire / `dotnet run`) | `appsettings.Development.json` defaults, `dotnet user-secrets`, environment variables. Docker secrets are skipped when `ASPNETCORE_ENVIRONMENT=Development`. |
| Docker Compose / production | Docker secrets: files in `./secrets/*.txt` (git-ignored), mounted at `/run/secrets/<name>` |
| CI (GitHub Actions) | Repository **secrets** (sensitive) and **variables** (non-sensitive). See [CI-CD.md](CI-CD.md#github-setup). |

## Goal

Supply runtime credentials without committing values and preserve protected data during rotation.

## Prerequisites

Run commands from the repository root with OpenSSL and Docker available. Review `docker-compose.yml`, the selected
overlays and `secrets/README.md`; a file is not mounted merely because it exists on the host.

## Steps

1. Choose the environment source from the table above.
2. Create missing files, review mounts and protect permissions using the procedure below.
3. Configure non-secret settings and any intentionally environment-based credentials.
4. Recreate affected containers after a change and verify authentication and existing protected payloads.

### Docker secrets

`src/Api/Infrastructure/DockerSecretsConfigurationProvider.cs` (`builder.Configuration.AddDockerSecrets()`, early in `Program.cs`) loads every file in `/run/secrets` into configuration:

- **Known short names** map to configuration keys:

  | Secret file | Configuration key(s) |
  |---|---|
  | `jwt_secret` | `JWT_SECRET`, `JwtSettings:SecretKey` (warns if shorter than 32 chars) |
  | `postgres_password` | `POSTGRES_PASSWORD`, also builds `ConnectionStrings:starterkit` from `POSTGRES_HOST/PORT/DB/USER` |
  | `redis_password` | `REDIS_PASSWORD`, also builds `ConnectionStrings:Redis` from `REDIS_HOST/PORT` |
  | `identity_otp_hmac_key` | `Identity:OtpHmacKey` |
  | `hangfire_dashboard_password` | `Hangfire:Dashboard:Password` |
  | `turnstile_secret` | `BotProtection:SecretKey` |
  | `smtp_password` | `Email:SmtpPassword` |
  | `rabbitmq_password` | `Messaging:RabbitMq:Password` |
  | `dataprotection_cert_password` | `Identity:DataProtection:Certificate:Password` |

- **Migration connection string.** Compose maps the secret `migration_connection_string`
  (`secrets/migration_connection_string.txt`, may be empty) into the migrator container as
  `/run/secrets/Database__MigrationConnectionString`, which becomes `Database:MigrationConnectionString`. It is a
  secret, not an env var, so it does not show in `docker inspect`.
- **Any other file** becomes a key from its name, with `__` turned into `:`. For example, `Email__SmtpPassword`
  maps to `Email:SmtpPassword`.
- **Binary and key files** (`.pfx`, `.p12`, `.json`, `.p8`) are not loaded as values. They are referenced by path, for
  example `Identity:DataProtection:Certificate:Path=/run/secrets/dataprotection_cert.pfx` or the FCM service-account
  key `Push:CredentialsFile=/run/secrets/fcm_service_account.json` (`docker-compose.push.yml`): the APNs key `Push:Apns:PrivateKeyFile=/run/secrets/apns_auth_key.p8` is also read by path, not loaded as configuration text.

### Creating the secret files

The file list and generation commands are in [`secrets/README.md`](../../secrets/README.md). Use the
helper script:

```bash
./deployment/secrets-setup.sh          # creates any missing secrets/*.txt with strong random values
chmod 600 secrets/*
```

The base file requires `postgres_password.txt`, `redis_password.txt`, `jwt_secret.txt`,
`identity_otp_hmac_key.txt` and `migration_connection_string.txt` (the last may be empty). Optional overlays add
`rabbitmq_password.txt`, `dataprotection_cert.pfx` / `dataprotection_cert_password.txt`, `fcm_service_account.json`
and `apns_auth_key.p8`. `turnstile_secret.txt` needs an explicit mount when used; the provider's name mapping
does not add a Compose secret mount automatically.

The provider's legacy `hangfire_dashboard_password` mapping is `Hangfire:Dashboard:Password`, while the host
auth options use `Hangfire:DashboardAuth:Password`. The shipped Compose file sets
`Hangfire__DashboardAuth__Password` from `HANGFIRE_DASHBOARD_PASSWORD`; use that or the generic secret target
`Hangfire__DashboardAuth__Password` rather than relying on the legacy mapping.

Rotation rules:

- Generate values with `openssl rand -base64 32` (48 for the JWT key), with no trailing newline.
- **Don't rotate `dataprotection_cert.pfx` casually.** It encrypts the Data Protection key ring, and replacing it
  invalidates existing protected payloads such as auth cookies and antiforgery tokens.
- Coordinate `jwt_secret` rotation with `JwtSettings:PreviousSecretKeys` when existing access tokens must remain
  valid until expiry. Without retaining the old key, tokens it signed stop validating. Refresh tokens are opaque,
  database-backed values: signing-key rotation alone does not revoke refresh sessions. See `JwtSigningKeys` in
  `src/Modules/Identity/Identity/Services/Token/JwtSigningKeys.cs`.
- Back up `secrets/` securely (password manager or vault). It is not in git and not in the database backups.

### Environment settings

`.env.prod` on the server (from `deployment/.env.production.example`) holds non-secret settings:

- the domain, image and tag
- Postgres user and DB names
- S3 bucket and region for backups
- the messaging transport

Despite that separation, `HANGFIRE_DASHBOARD_PASSWORD`, `GRAFANA_ADMIN_PASSWORD`, exporter passwords and AWS keys for pgBackRest also use environment interpolation today. Environment values are visible to container inspection; protect `.env.prod` as sensitive. Prefer an instance role or IAM user scoped to the backup bucket.

## Verify

Check resolved secret mounts without printing secret values. Empty/unreadable files are skipped by the provider;
startup logs list names/counts, not values. Confirm the app starts, can authenticate and can use the database/cache.

### Checklist

- [ ] Every `CHANGE_ME` in `.env.prod` is replaced.
- [ ] `secrets/*.txt` exist, are `chmod 600`, and are not tracked (`git status` is clean).
- [ ] The API log at startup shows `[secrets] Loaded N secret(s): ...` with the expected names.
- [ ] Seq, Grafana and the Hangfire dashboard use non-default passwords (see [Seq-Security.md](Seq-Security.md)).

## Pitfalls

Changing a PostgreSQL password file does not alter an existing database role password. Rotate both sides in a
coordinated operation. Replacing a Data Protection certificate without preserving decryptability can strand the
key ring; back up certificates, passwords and the ring separately from database backups. Seq's first-run hash
does not reset a user in an existing Seq volume; see [Seq security](Seq-Security.md).

Configuration-provider defaults remain subject to `ProductionConfigurationGuard`; placeholders are not usable
production secrets. Development skips Docker-secret loading. Avoid trailing newlines when generating values;
the provider trims text and skips binary/key files and hidden files.

## Related

[Compose](Docker-Compose.md), [CI/CD](CI-CD.md#github-setup), [migrations](Database-Migrations.md),
[Identity](../Identity/README.md), and `/starterkit:update-docs`.
