# GitHub Runner Maintenance Guide

> **Host-Based Runner** (migrated 2026-02-02)

This guide covers daily operations, maintenance tasks, and troubleshooting for the host-based GitHub Actions runner on your production VPS.

---

## Quick Reference

### Service Information
- **Service Name**: `actions.runner.ubuntu-<hostname>.service`
- **Installation Path**: `~/actions-runner/`
- **User**: `ubuntu`
- **Type**: Host-based (not containerized)
- **Labels**: `self-hosted`, `deploy-only`, `vps`, `production`

---

## Service Management

### Basic Commands

```bash
# Get service name (run this first)
SERVICE="actions.runner.$(whoami)-$(hostname).service"

# Start runner
sudo systemctl start $SERVICE

# Stop runner
sudo systemctl stop $SERVICE

# Restart runner
sudo systemctl restart $SERVICE

# Check status
sudo systemctl status $SERVICE

# Enable auto-start on boot
sudo systemctl enable $SERVICE

# Disable auto-start
sudo systemctl disable $SERVICE
```

### View Logs

```bash
SERVICE="actions.runner.$(whoami)-$(hostname).service"

# View logs (live follow)
sudo journalctl -u $SERVICE -f

# View last 50 lines
sudo journalctl -u $SERVICE -n 50

# View last 100 lines (no pager)
sudo journalctl -u $SERVICE -n 100 --no-pager

# View logs with timestamps
sudo journalctl -u $SERVICE -n 100 -o short-iso

# View logs from specific time
sudo journalctl -u $SERVICE --since "1 hour ago"
sudo journalctl -u $SERVICE --since "2024-01-01"

# View logs between times
sudo journalctl -u $SERVICE --since "2024-01-01" --until "2024-01-02"

# Search logs for specific text
sudo journalctl -u $SERVICE | grep "error"
sudo journalctl -u $SERVICE | grep -i "listening for jobs"
```

---

## Health Checks

### Quick Health Check

```bash
# Check service status
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl status $SERVICE

# Expected output:
# ● actions.runner.ubuntu-<hostname>.service
#    Active: active (running)
#    Main PID: <number>
```

### Comprehensive Health Check

```bash
# 1. Check runner process
ps aux | grep Runner.Listener
# Should show Runner.Listener process

# 2. Check service status
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl is-active $SERVICE
# Expected: active

# 3. Check if enabled for boot
sudo systemctl is-enabled $SERVICE
# Expected: enabled

# 4. Check recent logs
sudo journalctl -u $SERVICE -n 20
# Should show "Listening for Jobs"

# 5. Check GitHub UI
# Go to: https://github.com/your-org/StarterKit/settings/actions/runners
# Runner should show: 🟢 Idle (or 🔵 Active if running job)

# 6. Test Docker access
docker ps
# Should list running containers

# 7. Test filesystem access
ls -la /opt/starterkit/
# Should show deployment files

# 8. Test localhost access
curl http://localhost:5000/health
# Should return API health status (if API is running)

# 9. Run deployment health check
cd /opt/starterkit
./deployment/scripts/health-check.sh
# All checks should pass
```

---

## Troubleshooting

### Runner Shows Offline in GitHub

**Symptoms**: Runner shows 🔴 Offline in GitHub UI

**Diagnosis**:
```bash
SERVICE="actions.runner.$(whoami)-$(hostname).service"

# Check service status
sudo systemctl status $SERVICE

# Check logs for errors
sudo journalctl -u $SERVICE -n 100

# Check network connectivity
curl -I https://api.github.com
# Expected: HTTP/2 200

# Check if process is running
ps aux | grep Runner.Listener
```

**Solutions**:
```bash
# Solution 1: Restart service
sudo systemctl restart $SERVICE

# Solution 2: Check token hasn't expired
cd ~/actions-runner
# If token expired, reconfigure (see Token Rotation section)

# Solution 3: Reload systemd if service file changed
sudo systemctl daemon-reload
sudo systemctl restart $SERVICE

# Solution 4: Check firewall
sudo ufw status
# Ensure outbound HTTPS (443) is allowed
```

---

### Jobs Not Being Picked Up

**Symptoms**: Jobs queue but runner doesn't pick them up

**Diagnosis**:
```bash
# 1. Check runner is Idle (not Offline)
# GitHub UI: Settings → Actions → Runners
# Should show 🟢 Idle

# 2. Check runner labels
# GitHub UI should show: self-hosted, Linux, X64, deploy-only, vps, production

# 3. Verify workflow uses correct labels
# In .github/workflows/hybrid-ci-cd.yml:
# runs-on: [self-hosted, deploy-only]

# 4. Check runner logs
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo journalctl -u $SERVICE -f
# Watch for job pickup messages
```

**Solutions**:
```bash
# Solution 1: Verify labels match
# Workflow must use: runs-on: [self-hosted, deploy-only]
# Runner must have label: deploy-only

# Solution 2: Restart runner
sudo systemctl restart $SERVICE

# Solution 3: Check token
# If token expired, see Token Rotation section
```

---

### Permission Errors

**Symptoms**: Deployment fails with "Permission denied"

**Diagnosis**:
```bash
# Check /opt/starterkit ownership
ls -ld /opt/starterkit
# Should show: drwxr-xr-x ubuntu ubuntu

# Check Docker group membership
groups ubuntu | grep docker
# Should include 'docker'

# Test Docker without sudo
docker ps
# Should work without permission error
```

**Solutions**:
```bash
# Fix filesystem permissions
sudo chown -R ubuntu:ubuntu /opt/starterkit
sudo chmod 755 /opt/starterkit

# Add ubuntu to docker group
sudo usermod -aG docker ubuntu
newgrp docker

# Test Docker access
docker ps

# If still fails, restart runner
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl restart $SERVICE
```

---

### Localhost Connection Errors

**Symptoms**: Health checks fail, "Connection refused" to localhost

**Diagnosis**:
```bash
# Check if API container is running
docker compose ps
# Should show api container as 'Up'

# Test localhost access
curl http://localhost:5000/health
# If connection refused, API may not be running

# Check Docker network
docker network ls
# Should show starterkit network

# Check API port binding
docker port $(docker ps -q -f name=api)
# Should show 5000/tcp -> 0.0.0.0:5000
```

**Solutions**:
```bash
# Solution 1: Start API containers
cd /opt/starterkit
docker compose up -d

# Solution 2: Check API logs
docker compose logs api --tail=50

# Solution 3: Restart API container
docker compose restart api

# Solution 4: Full restart
docker compose down
docker compose up -d
```

---

### Runner Crashes or Stops Unexpectedly

**Symptoms**: Runner service stops on its own

**Diagnosis**:
```bash
SERVICE="actions.runner.$(whoami)-$(hostname).service"

# Check service status
sudo systemctl status $SERVICE

# Check crash logs
sudo journalctl -u $SERVICE -n 200

# Check system logs
dmesg | tail -50

# Check disk space
df -h
# /opt/starterkit should have space available

# Check memory
free -h
```

**Solutions**:
```bash
# Solution 1: Restart service
sudo systemctl restart $SERVICE

# Solution 2: Check for out-of-memory issues
# Review logs for OOM errors
sudo journalctl -u $SERVICE | grep -i "memory"

# Solution 3: Clean up disk space
docker system prune -a  # WARNING: Removes unused images
sudo journalctl --vacuum-time=7d

# Solution 4: Reinstall runner (if corrupted)
# See "Reinstall Runner" section below
```

---

## Maintenance Tasks

### Daily

```bash
# Quick status check
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl status $SERVICE

# Should show: active (running)
```

### Weekly

```bash
# Review recent logs
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo journalctl -u $SERVICE -n 100

# Check disk space
df -h

# Check runner in GitHub UI
# https://github.com/your-org/StarterKit/settings/actions/runners
# Should show: 🟢 Idle
```

### Monthly

```bash
# Clean old logs
sudo journalctl --vacuum-time=30d

# Check for runner updates
cd ~/actions-runner
./config.sh --version
# Compare with: https://github.com/actions/runner/releases

# Review Docker disk usage
docker system df

# Clean unused Docker resources (optional)
docker system prune  # Remove stopped containers, unused networks
```

### Quarterly (Every 90 Days)

```bash
# Rotate PAT token (REQUIRED)
# See "Token Rotation" section below

# Update runner if new version available
# See "Update Runner Version" section below

# Review runner logs for patterns
sudo journalctl -u actions.runner.* --since "90 days ago" | grep -i error
```

---

## Token Rotation

GitHub PAT tokens expire every 90 days. Follow this process to rotate:

### Before Expiration

Set a calendar reminder for 1 week before token expiration.

### Rotation Process

```bash
# Step 1: Generate new PAT token in GitHub
# Go to: GitHub → Settings → Developer settings → Personal access tokens
# Generate new token with same scopes: repo, workflow, admin:repo_hook
# Copy the new token

# Step 2: Stop runner
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl stop $SERVICE

# Step 3: Remove old configuration
cd ~/actions-runner
./config.sh remove --token OLD_TOKEN

# Step 4: Reconfigure with new token
./config.sh \
  --url https://github.com/your-org/StarterKit \
  --token NEW_REGISTRATION_TOKEN \
  --name starterkit-vps-host-runner \
  --labels self-hosted,deploy-only,vps,production \
  --work _work \
  --replace

# Step 5: Start runner
sudo systemctl start $SERVICE

# Step 6: Verify
sudo systemctl status $SERVICE
sudo journalctl -u $SERVICE -n 20

# Step 7: Verify in GitHub UI
# Go to: https://github.com/your-org/StarterKit/settings/actions/runners
# Runner should show: 🟢 Idle

# Step 8: Delete old token from GitHub
# Go to: GitHub → Settings → Developer settings → Personal access tokens
# Delete the old token
```

---

## Update Runner Version

When a new runner version is released:

### Check Current Version

```bash
cd ~/actions-runner
./config.sh --version
# Example: 2.311.0

# Check latest version:
# https://github.com/actions/runner/releases
```

### Update Process

```bash
# Step 1: Stop runner
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl stop $SERVICE

# Step 2: Backup current installation
cd ~
cp -r actions-runner actions-runner-backup-$(date +%Y%m%d)

# Step 3: Download new version
cd ~/actions-runner
RUNNER_VERSION="2.XX.X"  # Replace with new version number
curl -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz -L \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Step 4: Extract (overwrites binaries, keeps configuration)
tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Step 5: Start runner
sudo systemctl start $SERVICE

# Step 6: Verify
sudo systemctl status $SERVICE
./config.sh --version
# Should show new version

# Step 7: Monitor first job
sudo journalctl -u $SERVICE -f
# Watch a deployment complete successfully

# Step 8: Clean up backup (after 1 week of stable operation)
rm -rf ~/actions-runner-backup-*
```

---

## Reinstall Runner

If runner becomes corrupted or needs fresh installation:

### Backup First

```bash
# Backup current configuration
cd ~/actions-runner
cp .credentials ~/runner-credentials-backup
cp .runner ~/runner-config-backup
```

### Reinstall Process

```bash
# Step 1: Stop and uninstall service
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl stop $SERVICE
cd ~/actions-runner
sudo ./svc.sh uninstall

# Step 2: Remove runner from GitHub
./config.sh remove --token YOUR_TOKEN

# Step 3: Remove runner directory
cd ~
rm -rf ~/actions-runner

# Step 4: Reinstall from scratch
# Follow VPS-RUNNER-SETUP.md from beginning

# Or quick reinstall:
mkdir -p ~/actions-runner && cd ~/actions-runner
RUNNER_VERSION="2.311.0"
curl -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz -L \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz
tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

./config.sh \
  --url https://github.com/your-org/StarterKit \
  --token YOUR_REGISTRATION_TOKEN \
  --name starterkit-vps-host-runner \
  --labels self-hosted,deploy-only,vps,production \
  --work _work \
  --replace

sudo ./svc.sh install ubuntu
sudo systemctl enable $SERVICE
sudo systemctl start $SERVICE

# Step 5: Verify
sudo systemctl status $SERVICE
```

---

## Monitoring and Alerts

### Log Monitoring

```bash
# Watch for errors in real-time
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo journalctl -u $SERVICE -f | grep -i error

# Daily error summary
sudo journalctl -u $SERVICE --since "1 day ago" | grep -i error | wc -l

# Check for specific issues
sudo journalctl -u $SERVICE --since "1 week ago" | grep -i "connection"
sudo journalctl -u $SERVICE --since "1 week ago" | grep -i "timeout"
```

### Automated Monitoring (Optional)

Set up cron job for daily health check:

```bash
# Create monitoring script
cat > ~/check-runner-health.sh <<'EOF'
#!/bin/bash
SERVICE="actions.runner.ubuntu-$(hostname).service"

if ! systemctl is-active --quiet $SERVICE; then
    echo "ALERT: Runner service is not active!"
    # Add notification here (email, Slack, etc.)
    sudo systemctl start $SERVICE
fi
EOF

chmod +x ~/check-runner-health.sh

# Add to crontab
crontab -e
# Add line:
# 0 * * * * ~/check-runner-health.sh
```

---

## Emergency Procedures

### Runner Completely Unresponsive

```bash
# Step 1: Force stop
SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl kill $SERVICE
sudo systemctl stop $SERVICE

# Step 2: Clean stale processes
pkill -9 -f Runner.Listener

# Step 3: Reload systemd
sudo systemctl daemon-reload

# Step 4: Restart
sudo systemctl start $SERVICE

# Step 5: Verify
sudo systemctl status $SERVICE
sudo journalctl -u $SERVICE -f
```

### System Reboot

After VPS reboot:

```bash
# Runner should auto-start via systemd
# Wait 2 minutes, then check:

SERVICE="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl status $SERVICE

# If not running:
sudo systemctl start $SERVICE

# Verify in GitHub UI
# Should show 🟢 Idle within 5 minutes
```

---

## Useful Commands Summary

```bash
# Quick health check
sudo systemctl status actions.runner.*

# Watch logs
sudo journalctl -u actions.runner.* -f

# Restart runner
sudo systemctl restart actions.runner.*

# Check GitHub connectivity
curl -I https://api.github.com

# Test deployment capabilities
docker ps
curl http://localhost:5000/health
ls -la /opt/starterkit/
cd /opt/starterkit && ./deployment/scripts/health-check.sh

# Clean logs
sudo journalctl --vacuum-time=30d

# Check disk space
df -h
docker system df
```

---

## Support Resources

- **GitHub Actions Documentation**: https://docs.github.com/en/actions
- **Self-Hosted Runners**: https://docs.github.com/en/actions/hosting-your-own-runners
- **Runner Releases**: https://github.com/actions/runner/releases
- **VPS Setup Guide**: `wiki/deployment/VPS-RUNNER-SETUP.md`
- **GitHub Setup Guide**: `wiki/deployment/GITHUB-RUNNER-SETUP.md`

---

## Checklist: Healthy Runner

- [ ] Service status: active (running)
- [ ] GitHub UI: 🟢 Idle
- [ ] Logs show: "Listening for Jobs"
- [ ] Enabled for boot: `sudo systemctl is-enabled actions.runner.*` → enabled
- [ ] Docker access works: `docker ps`
- [ ] Localhost access works: `curl http://localhost:5000/health`
- [ ] Filesystem access works: `ls -la /opt/starterkit/`
- [ ] Token not expired (check 90-day rotation schedule)
- [ ] Disk space available: `df -h` shows >20% free
- [ ] Recent deployments successful (check GitHub Actions)

---

**Maintain this runner regularly to ensure reliable CI/CD operations!** 🚀
