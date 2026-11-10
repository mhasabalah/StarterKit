# Self-hosted GitHub Actions runner

Install and maintain a host runner with direct access to Docker, deployment files and local health endpoints on the VPS.

## Goal

Make approved production deployment, rollback, backup and resource checks runnable on `[self-hosted, deploy-only]`.

## Prerequisites

- A trusted Linux deployment host, sudo access and a non-root deploy account (examples use `ubuntu`).
- Docker daemon access without sudo, Compose 2.24+, at least 2 GB free disk and outbound HTTPS to GitHub/GHCR.
- Writable `/opt/starterkit` (or the configured `DEPLOY_DIR`) and `/backup`; runtime secrets remain outside git.
- Repository administration access and the [production environment/variables](CI-CD.md#github-setup).

## Steps

1. Prepare the deploy account and directories. Docker group membership grants control of the host; give this
   runner only trusted deployment work. Re-login or restart its service after changing group membership.

   ```bash
   sudo usermod -aG docker ubuntu
   docker ps
   bash deployment/scripts/verify-runner.sh /opt/starterkit
   ```

   `verify-runner.sh` is read-only and is also used by CI and `test-runner.yml`. Resolve missing CLI, daemon,
   Compose, disk or directory checks before registering the runner.

2. In repository Settings → Actions → Runners → New self-hosted runner, select the host OS/architecture.
   Use the displayed download and checksum commands for that runner release; do not copy a historical binary
   version or hash from documentation. Extract it into `~/actions-runner`.

3. Use the short-lived registration token from that page with the displayed `config.sh` command. Set the
   `deploy-only` label; the original deployment convention also uses `vps` and `production`.
   Keep the installation on the host, not inside the application Compose network. The runner needs direct
   localhost access to the published API port and filesystem access to `DEPLOY_DIR`.

4. Install and start the runner's system service from the extracted directory:

   ```bash
   sudo ./svc.sh install ubuntu
   sudo ./svc.sh start
   sudo ./svc.sh status
   ```

   Use the actual unit name created by the installer for `systemctl` and `journalctl`; do not derive it from
   `whoami` and hostname. Verify boot enablement and note the installation directory, account and unit in the
   deployment inventory.

5. Run Actions → Test Self-Hosted Runner, then perform an approved release. Verify filesystem/Docker access,
   prerequisite checks and local/public health checks in the workflow logs.

## Verify

```bash
docker ps
bash deployment/scripts/verify-runner.sh /opt/starterkit
curl -f http://localhost:5000/health
sudo nginx -t
df -h
```

The runner should show Idle or Active in repository settings and its service logs should show job pickup.
The health URL requires a running API; an empty host can pass runner prerequisites before the first release.

## Pitfalls

| Symptom | Check / action |
|---|---|
| Offline | Actual service status/logs, listener process, outbound HTTPS and host clock; restart the service after fixing the cause |
| Jobs remain queued | Runner online and `deploy-only` label matches workflow; check environment approval separately |
| Permission denied | Deploy/backup directory ownership and the service account's Docker group membership |
| Local connection refused | API/migrator logs, published port and `/health`; a runner restart cannot fix API readiness |
| Service crashes | Host OOM, free disk, runner diagnostics and system journal before restarting |
| Configuration changed | Reload systemd if its unit changed, restart and re-run the runner smoke workflow |

### Maintenance and recovery

Review service state daily, logs/disk and recent deployments weekly, and runner updates and Docker disk usage
monthly. Retain enough images and backups for recovery before pruning; indiscriminate volume cleanup destroys
state. Monitor runner availability independently if deployments depend on it.

Use the supported runner update process for the installed version and verify its next job. Before reinstalling,
stop/uninstall the service and remove the repository registration using the current removal token; then repeat
registration and service installation. Protect `.runner` and `.credentials` as credentials, rather than copying
them into an unprotected home-directory backup. After a reboot, verify the actual service starts and the runner
returns online before relying on scheduled backups.

Runner registration tokens and PATs have different purposes. Registration uses a short-lived registration token;
an installed runner is not maintained by re-registering every 90 days with a PAT. Rotate any separately configured
PAT according to its actual expiry and use; do not assume a universal lifetime or reuse a PAT as a registration token.
For removal/re-registration, obtain the matching current token from repository settings.

## Related

[CI/CD](CI-CD.md), [VPS deployment](Deploy-to-VPS.md), [backups](Backups.md),
[`deployment/scripts/verify-runner.sh`](../../deployment/scripts/verify-runner.sh), and `/starterkit:update-docs`.
