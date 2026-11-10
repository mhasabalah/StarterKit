# Deploy to a VPS

Provision a Linux VPS for the Compose runtime, host Nginx and an approved deployment from GHCR.

## Goal

Serve `https://api.example.com` with TLS, private infrastructure access, deploy-time migrations and tested recovery.

## Prerequisites

- Ubuntu host with sudo/SSH access, Docker Engine, Compose 2.24+ and enough memory for the selected overlays.
- A domain whose A record resolves to `YOUR_SERVER_IP`; wildcard DNS and a matching certificate if using tenant subdomains.
- A deployment checkout in `DEPLOY_DIR` (workflow default `/opt/starterkit`), owned by the deploy user.
- [Secrets](Secrets.md), GHCR image access and, for CI releases, [runner setup](Self-Hosted-Runner.md) and [GitHub setup](CI-CD.md#github-setup).
- An S3 backup plan covering shared and dedicated databases; see [Backups](Backups.md).

## Steps

1. Configure DNS and the firewall. Permit 80/443 and restricted SSH; review Docker-published ports as well as
   host firewall rules. Confirm DNS before requesting a certificate:

   ```bash
   dig +short api.example.com
   ```

2. Install the host proxy and certificate tooling:

   ```bash
   sudo apt update
   sudo apt install nginx certbot python3-certbot-nginx -y
   docker --version
   docker compose version
   bash deployment/scripts/verify-runner.sh /opt/starterkit
   ```

3. Prepare `.env.prod` from `deployment/.env.production.example` and run `./deployment/secrets-setup.sh`
   from the repository root. Replace placeholders and select an image tag. The base Compose file sets the API
   environment to `Docker`; setting `ASPNETCORE_ENVIRONMENT=Production` only in `.env.prod` does not change that
   literal. Both environments are subject to the production configuration guard.

4. Bootstrap TLS with a temporary HTTP-only host site for `api.example.com`, then run:

   ```bash
   sudo mkdir -p /var/www/certbot
   sudo chown -R www-data:www-data /var/www/certbot
   sudo certbot --nginx -d api.example.com
   ```

   Do this before installing the shipped HTTPS site: it references certificate files and Certbot include files
   immediately. Installing it first on a fresh host makes `nginx -t` fail. See [Nginx](Nginx.md#ssltls-configuration).

5. Review and install `deployment/nginx/nginx.conf` and `conf.d/app-production.conf`. Update the domain,
   certificate paths, trusted origins for Nginx-generated errors, allowed administrator addresses and upstream
   listener. The shipped upstream is `localhost:5000`, not a pair of replicas.

   ```bash
   sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf
   sudo cp deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit
   sudo rm -f /etc/nginx/sites-enabled/default
   sudo ln -sf /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/starterkit
   sudo nginx -t
   sudo systemctl reload nginx
   sudo systemctl enable nginx
   ```

6. Start the selected Compose configuration. Explicit `-f` excludes the development override:

   ```bash
   docker compose -f docker-compose.yml --env-file .env.prod up -d
   docker compose -f docker-compose.yml --env-file .env.prod ps -a
   docker compose -f docker-compose.yml --env-file .env.prod logs migrator api
   ```

   The migrator must exit successfully before the API starts. Add the Data Protection overlay for encrypted
   keys and ClamAV for required malware scanning; choose full or lite telemetry using [Compose](Docker-Compose.md).
   Keep the same selected files for later operations. The stock CI deploy selects only the base file.

7. Configure Seq authentication, protected monitoring routes, backup initialization and production reviewers.
   Use [CI/CD](CI-CD.md) for routine releases; migrate explicitly before an existing API is replaced.

## Verify

```bash
curl -f http://localhost:5000/health
curl -f http://localhost:5000/alive
curl -I https://api.example.com/health
curl -I http://api.example.com/health
sudo certbot renew --dry-run
sudo systemctl status certbot.timer
docker stats
```

Expect readiness/liveness success, an HTTP-to-HTTPS redirect and valid certificate dates. Verify security headers
on the actual routed responses. Test rate limiting on an API route: `/health` and `/alive` deliberately bypass
Nginx request limits. Confirm restricted tools are inaccessible from an unapproved address and test a backup restore.

## Pitfalls

| Symptom | Investigation |
|---|---|
| DNS missing/wrong | Check registrar records and propagation; test from another resolver |
| 502/503 | Check API port, container logs, migration exit and Nginx upstream; inspect `starterkit-https-error.log` |
| Certificate failure | Check certificate files, domain and expiry before reloading; rehearse renewal |
| Tool returns 403 | Review its allow-list; Swagger/Scalar must also be enabled in the application |
| Unexpected 429 | Inspect `limiting requests` in the error log and the actual API/auth zone |
| High memory/disk | Inspect container limits, Docker usage, host memory and log retention before restarting services |
| Password changed but login fails | Existing PostgreSQL/Redis/Seq state is not reset by editing environment files |

### Routine maintenance

Use `docker compose -f docker-compose.yml --env-file .env.prod logs -f api` and service-specific logs for
PostgreSQL, Redis and Seq. Host access/error logs are under `/var/log/nginx/` with separate HTTP, HTTPS and hub
files. JSON access logs expose request/upstream duration for slow-request analysis; the hub format excludes tokens.
Review status-code counts, source addresses and rate-limit errors without copying sensitive query strings.

For Nginx changes, copy the reviewed files, run `sudo nginx -t`, then reload. Use `sudo certbot renew` for renewal
and inspect expiry and the timer. Restarting the whole Compose stack or Nginx interrupts traffic; prefer a validated
reload or service-specific operation. Record the selected image and backup before any replacement. Logical dumps
and physical backup inspection are in [Backups](Backups.md).

## Related

[Deployment overview](README.md), [Nginx](Nginx.md), [Secrets](Secrets.md), [production testing](Production-Testing.md),
[migrations](Database-Migrations.md), and `/starterkit:add-migration`.
