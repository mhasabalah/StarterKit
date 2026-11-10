# Secure Seq access

Configure Seq's initial administrator credential and restrict network ingress before exposing operational logs.

## Goal

Allow authorized log searches while preventing direct unauthenticated access to the Seq UI and its stored data.

## Prerequisites

Use the deployment checkout with Docker/OpenSSL and `.env.prod`. The base service uses `datalust/seq:2024.3`
and persists `/data` in `seq-data`. Know whether this is a fresh or already initialized volume.

## Steps

1. Generate a password/hash before the first start. From the repository root:

   ```bash
   ./deployment/secure-seq.sh
   ```

   The helper writes `secrets/seq_admin_password.txt` relative to the current directory and prints the password
   and hash. Run it in a private terminal and protect the file/terminal output. It does not edit `.env.prod`.
   Its output provides the `SEQ_ADMIN_PASSWORD_HASH` assignment to set there.

2. For manual generation, use `openssl rand -base64 32` and the Seq image's `config hash` command, then store the
   resulting hash in `SEQ_ADMIN_PASSWORD_HASH`. The base file maps that variable to
   `SEQ_FIRSTRUN_ADMINPASSWORDHASH`; it is a **first-run** initialization setting, not a password-reset API.

3. Create or recreate the selected service to apply changed environment configuration:

   ```bash
   docker compose -f docker-compose.yml --env-file .env.prod up -d --no-deps seq
   ```

   A plain `restart` does not reload environment values. An existing initialized Seq volume retains its users;
   changing the first-run hash does not reset them. Use Seq's supported account recovery for that installation
   instead of deleting the log volume as a routine recovery procedure.

4. Restrict the published `SEQ_PORT` and `/seq/` proxy location. Base Compose publishes port 5341 by default
   without a loopback bind; Nginx's deny rule does not protect direct port access. Bind/restrict it in the deployed
   configuration, firewall or private network and require authorized access through a VPN/tunnel or reviewed proxy.

5. The shipped host `/seq/` location denies all. Add only intended administrator addresses before `deny all`,
   validate Nginx and reload. Basic authentication is an optional additional layer, **not already active** in
   `deployment/nginx/conf.d/app-production.conf`. If adding it, provision/protect the password file, preserve
   the existing proxy headers and verify both UI access and ingestion from the API.

## Verify

- A fresh Seq UI prompts for administrator login; use `admin` and the generated password.
- An unapproved address cannot access either the proxy path or the published port directly.
- Authorized access works through the intended route, and API logs/traces still arrive.
- Inspect environment key presence and container logs without printing credentials or the full secret-bearing environment.

## Pitfalls

| Symptom | Investigation |
|---|---|
| No login prompt | Existing volume/user state, first-run hash and whether the container was recreated |
| Wrong password | Correct initialized instance and credential; first-run changes do not reset existing users |
| Proxy 403 | Intended IP allow-list and `deny all`; review the installed site, then `sudo nginx -t` and reload |
| Proxy works but direct port is public | Docker publication/firewall; proxy authentication covers only its route |
| API no longer logs | Seq endpoint/ingestion configuration and exporter logs; not proof of API readiness failure |

Keep logs and credential backups separate from database backups. Removing `seq-data` deletes logs and users;
it is a destructive reinitialization, not a default password-reset step. Avoid committing secrets or retaining
the helper's printed credential in CI logs.

## Related

[Secrets](Secrets.md), [Nginx](Nginx.md), [observability](Observability.md),
[Seq authentication](https://docs.datalust.co/docs/authentication), and `/starterkit:update-docs`.
