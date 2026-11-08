# Self-Hosted GitHub Actions Runner

The deploy, rollback, backup and health workflows run on a **host-based** self-hosted runner on the VPS, labelled
`deploy-only` (`runs-on: [self-hosted, deploy-only]`). This page covers [setup](#part-1-setup) and
[maintenance](#part-2-maintenance).

# Part 1: Setup


> **✅ MIGRATED**: This guide reflects the **host-based runner** architecture (migrated 2026-02-02)

This guide walks you through installing and configuring the GitHub Actions self-hosted runner **directly on your production VPS host**, not in Docker.

## Prerequisites

✅ Before starting, ensure you have:
- Root or sudo access to your VPS
- Docker and Docker Compose installed on the host
- At least 2GB free disk space
- Network access to GitHub (port 443)
- GitHub repository configured: `production` environment, secrets and variables (see [CI-CD.md](CI-CD.md#github-setup))
- Your GitHub PAT token ready

---

## Architecture: Host-Based Runner

```
VPS Host (YOUR_SERVER_IP / api.example.com)
├── ubuntu user
│   └── ~/actions-runner/
│       ├── Runner binaries
│       └── systemd service: actions.runner.ubuntu-<hostname>.service
├── Docker Engine (native access, no socket mounting)
├── /opt/starterkit/ (direct filesystem access)
└── Nginx (direct systemctl access)
```

**Benefits:**
- ✅ Direct localhost access (no network complexity)
- ✅ Native Docker commands (no socket mounting)
- ✅ Simple file permissions (no UID mapping)
- ✅ Easy debugging (no `docker exec` layer)
- ✅ Better performance (no container overhead)
- ✅ Same security (both approaches have Docker access)

---

## Part 1: VPS Environment Preparation

### Step 1: Connect to Your VPS

```bash
# SSH into your VPS
ssh ubuntu@YOUR_SERVER_IP

# Or use your key
ssh -i ~/.ssh/your-key.pem ubuntu@YOUR_SERVER_IP
```

### Step 2: Verify Docker Installation

```bash
# Check Docker version
docker --version
# Expected: Docker version 24.0+ or higher

# Check Docker Compose version
docker compose version
# Expected: Docker Compose version v2.20+ or higher

# Verify Docker is running
sudo systemctl status docker

# Test Docker without sudo
docker ps
# If this fails, add user to docker group:
sudo usermod -aG docker ubuntu
newgrp docker
```

Once the repository is on the host, one read-only script checks all of this at once (Docker CLI, daemon access, Compose
2.24+, free disk, the deploy directory). The deploy job runs it too, and so does the **Test Self-Hosted Runner** workflow:

```bash
bash deployment/scripts/verify-runner.sh /opt/starterkit
```

### Step 3: Verify Directory Access

```bash
# Verify /opt/starterkit directory
ls -ld /opt/starterkit
# Should show: drwxr-xr-x ubuntu ubuntu

# If permissions are wrong:
sudo chown -R ubuntu:ubuntu /opt/starterkit

# Create backup directory if needed
sudo mkdir -p /backup
sudo chown ubuntu:ubuntu /backup
```

### Step 4: Verify Network Access

```bash
# Test GitHub connectivity
curl -I https://api.github.com
# Expected: HTTP/2 200

# Test container registry
curl -I https://ghcr.io
# Expected: HTTP/2 200 or 301

# Test localhost access (important for health checks)
curl -I http://localhost:5000
# Should reach your API if it's running
```

---

## Part 2: Install GitHub Actions Runner

### Step 5: Download GitHub Actions Runner

```bash
# Create runner directory
mkdir -p ~/actions-runner && cd ~/actions-runner

# Download latest runner (check https://github.com/actions/runner/releases for updates)
RUNNER_VERSION="2.311.0"
curl -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz -L \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Validate hash (optional but recommended)
echo "29fc8cf2dab4c195bb147384e7e2c94cfd4d4022c793b346a6175435265aa278  actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz" | shasum -a 256 -c

# Extract
tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz
```

### Step 6: Get Runner Registration Token

**Option A: Generate new registration token from GitHub UI**
1. Go to: https://github.com/your-org/StarterKit/settings/actions/runners/new
2. Click "New self-hosted runner"
3. Copy the token from the configuration command

**Option B: Reuse existing PAT token**
- If you have a PAT token from the old Docker runner setup, you can reuse it
- Check `/etc/ephemeral-github-actions-runner.env` for the token value

### Step 7: Configure Runner

```bash
cd ~/actions-runner

# Configure runner (interactive)
./config.sh \
  --url https://github.com/your-org/StarterKit \
  --token YOUR_REGISTRATION_TOKEN_HERE \
  --name starterkit-vps-host-runner \
  --labels self-hosted,deploy-only,vps,production \
  --work _work \
  --replace

# Expected output:
# ✓ Runner successfully added
# ✓ Runner connection is good
```

**Important labels:**
- `self-hosted` - Automatically added by GitHub
- `deploy-only` - **CRITICAL** - Matches your workflow
- `vps` - For organization
- `production` - For environment identification

### Step 8: Install as Systemd Service

```bash
# Install service as ubuntu user
sudo ./svc.sh install ubuntu

# Service will be created at:
# /etc/systemd/system/actions.runner.ubuntu-<hostname>.service

# Verify service file created
ls /etc/systemd/system/actions.runner.*

# Expected output:
# /etc/systemd/system/actions.runner.ubuntu-<your-hostname>.service
```

### Step 9: Configure Sudo Access for Nginx

Your runner needs to reload Nginx without password prompts:

```bash
# Edit sudoers file
sudo visudo -f /etc/sudoers.d/github-runner

# Add this line:
ubuntu ALL=(ALL) NOPASSWD: /usr/sbin/nginx -s reload, /usr/bin/systemctl reload nginx, /usr/bin/systemctl restart nginx

# Save and exit (Ctrl+X, then Y, then Enter in nano)

# Verify syntax
sudo visudo -c -f /etc/sudoers.d/github-runner
# Expected: parsed OK
```

---

## Part 3: Start and Test Runner

### Step 10: Start Runner Service

```bash
# Get service name
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"
echo "Service name: $SERVICE_NAME"

# Start service
sudo systemctl start $SERVICE_NAME

# Check status
sudo systemctl status $SERVICE_NAME

# Expected output:
# Active: active (running)
# Main PID: <process-id>

# View logs
sudo journalctl -u $SERVICE_NAME -f
# Expected to see: "Listening for Jobs"
# Press Ctrl+C to exit logs
```

### Step 11: Enable Auto-Start on Boot

```bash
# Enable service for auto-start
sudo systemctl enable $SERVICE_NAME

# Verify enabled
sudo systemctl is-enabled $SERVICE_NAME
# Expected: enabled
```

### Step 12: Verify Runner in GitHub

1. Go to your GitHub repository: https://github.com/your-org/StarterKit
2. Navigate to: **Settings** → **Actions** → **Runners**
3. You should see your runner:
   - **Name**: `starterkit-vps-host-runner`
   - **Status**: 🟢 **Idle**
   - **Labels**: `self-hosted`, `Linux`, `X64`, `deploy-only`, `vps`, `production`

---

## Part 4: Test Runner Capabilities

### Step 13: Test Docker Access

```bash
# Test Docker commands
docker ps
docker version
docker compose version

# All should work without sudo
```

### Step 14: Test Filesystem Access

```bash
# Test access to deployment directory
ls -la /opt/starterkit/
cat /opt/starterkit/docker-compose.yml

# Create test file
touch /opt/starterkit/test-runner.txt
rm /opt/starterkit/test-runner.txt
```

### Step 15: Test Localhost Access

```bash
# Test localhost access (CRITICAL - this is why we moved to host!)
curl -sf http://localhost:5000/health

# If API is running, this should return health status
# If not running, you'll get "connection refused" - that's OK
```

### Step 16: Test Nginx Access

```bash
# Test Nginx commands
sudo nginx -t
# Expected: configuration file syntax is ok

# Test reload (should work without password)
sudo systemctl reload nginx
```

### Step 17: Test Health Check Script

```bash
# Run health check script
cd /opt/starterkit
./deployment/scripts/health-check.sh

# Should check:
# - API health endpoint
# - Database connectivity
# - Nginx status
```

---

## Part 5: Run First Test Deployment

### Step 18: Trigger Test Workflow

From GitHub UI:
1. Go to **Actions** tab
2. Select "Hybrid CI/CD" workflow
3. Click "Run workflow"
4. Select branch: `master`
5. Click "Run workflow"

### Step 19: Monitor Deployment

```bash
# On VPS, watch runner logs
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"
sudo journalctl -u $SERVICE_NAME -f

# In another terminal, watch containers
cd /opt/starterkit
watch -n 2 'docker compose ps'
```

Expected to see:
- Runner picks up job
- Deployment steps execute
- Health checks pass
- Job completes successfully ✅

---

## Part 6: Service Management

### Daily Operations

```bash
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"

# Start runner
sudo systemctl start $SERVICE_NAME

# Stop runner
sudo systemctl stop $SERVICE_NAME

# Restart runner
sudo systemctl restart $SERVICE_NAME

# Check status
sudo systemctl status $SERVICE_NAME

# View logs (last 50 lines)
sudo journalctl -u $SERVICE_NAME -n 50

# View logs (live follow)
sudo journalctl -u $SERVICE_NAME -f

# View logs with timestamps
sudo journalctl -u $SERVICE_NAME -n 100 --no-pager -o short-iso
```

### Health Checks

```bash
# Check if runner process is running
ps aux | grep Runner.Listener

# Check runner status in GitHub UI
# Repository → Settings → Actions → Runners
# Should show: 🟢 Idle (when not running jobs)

# Test capabilities
cd /opt/starterkit
./deployment/scripts/health-check.sh
```

---

## Part 7: Troubleshooting

### Runner Shows Offline in GitHub

```bash
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"

# Check service status
sudo systemctl status $SERVICE_NAME

# Check logs for errors
sudo journalctl -u $SERVICE_NAME -n 100

# Try restarting
sudo systemctl restart $SERVICE_NAME

# Check network connectivity
curl -I https://api.github.com
```

### Jobs Not Being Picked Up

1. **Verify labels match workflow**:
   ```yaml
   # In .github/workflows/hybrid-ci-cd.yml
   runs-on: [self-hosted, deploy-only]
   ```
   - Runner labels must include `deploy-only`

2. **Check runner is Idle** (not Offline) in GitHub UI

3. **Verify runner token hasn't expired**:
   - PAT tokens expire every 90 days
   - Check expiration date in GitHub Settings

### Permission Errors

```bash
# Fix /opt/starterkit permissions
sudo chown -R ubuntu:ubuntu /opt/starterkit
sudo chmod 755 /opt/starterkit

# Fix Docker group membership
sudo usermod -aG docker ubuntu
newgrp docker

# Test Docker access
docker ps
```

### Localhost Connection Refused

```bash
# Check if API containers are running
cd /opt/starterkit
docker compose ps

# Start containers if needed
docker compose up -d

# Test localhost access
curl http://localhost:5000/health
```

---

## Part 8: Maintenance

### Weekly

```bash
# Check runner status
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl status $SERVICE_NAME

# Review recent logs
sudo journalctl -u $SERVICE_NAME -n 100

# Check disk space
df -h
```

### Monthly

```bash
# Check for runner updates
cd ~/actions-runner
./config.sh --version

# Compare with latest release:
# https://github.com/actions/runner/releases

# Clean old logs
sudo journalctl --vacuum-time=30d
```

### Quarterly (Every 90 Days) - Token Rotation

```bash
# 1. Generate new PAT token in GitHub
# 2. Remove old runner configuration
cd ~/actions-runner
sudo systemctl stop actions.runner.$(whoami)-$(hostname).service
./config.sh remove --token OLD_TOKEN

# 3. Reconfigure with new token
./config.sh \
  --url https://github.com/your-org/StarterKit \
  --token NEW_REGISTRATION_TOKEN \
  --name starterkit-vps-host-runner \
  --labels self-hosted,deploy-only,vps,production \
  --work _work \
  --replace

# 4. Restart service
sudo systemctl start actions.runner.$(whoami)-$(hostname).service

# 5. Verify in GitHub UI
```

---

## Part 9: Updating Runner Version

```bash
# Stop runner
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl stop $SERVICE_NAME

# Backup current installation
cd ~
cp -r actions-runner actions-runner-backup

# Download new version
cd ~/actions-runner
RUNNER_VERSION="2.XX.X"  # Replace with new version
curl -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz -L \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Extract (overwrites binaries, keeps config)
tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Start runner
sudo systemctl start $SERVICE_NAME

# Verify
sudo systemctl status $SERVICE_NAME
./config.sh --version
```

---

## Part 10: Complete Uninstallation (If Needed)

```bash
# Stop service
SERVICE_NAME="actions.runner.$(whoami)-$(hostname).service"
sudo systemctl stop $SERVICE_NAME

# Uninstall service
cd ~/actions-runner
sudo ./svc.sh uninstall

# Remove runner from GitHub
./config.sh remove --token YOUR_TOKEN

# Remove runner directory
cd ~
rm -rf ~/actions-runner

# Verify removal in GitHub UI
# Repository → Settings → Actions → Runners
# Runner should be removed
```

---

## Next Steps

✅ **Runner is now installed!** Proceed to:
1. **Test end-to-end deployment** → Trigger a deployment from GitHub Actions
2. **Monitor first few deployments** → Watch logs and verify health checks
3. **Update team documentation** → Notify team of new deployment process
4. **Set up monitoring** → Consider adding alerts for runner downtime

---

## Quick Reference

### Service Commands
```bash
SERVICE="actions.runner.ubuntu-$(hostname).service"

# Manage service
sudo systemctl start $SERVICE
sudo systemctl stop $SERVICE
sudo systemctl restart $SERVICE
sudo systemctl status $SERVICE
sudo systemctl enable $SERVICE   # Auto-start on boot
sudo systemctl disable $SERVICE  # Disable auto-start

# View logs
sudo journalctl -u $SERVICE -f         # Follow mode
sudo journalctl -u $SERVICE -n 100     # Last 100 lines
```

### Health Check Commands
```bash
# Quick health check
cd /opt/starterkit && ./deployment/scripts/health-check.sh

# Test Docker access
docker ps

# Test localhost access
curl http://localhost:5000/health

# Test filesystem
ls -la /opt/starterkit/

# Test Nginx
sudo nginx -t
```

### GitHub UI Check
- Repository → Settings → Actions → Runners
- Expected: 🟢 **starterkit-vps-host-runner** - Idle

---

## Support & Resources

- **GitHub Actions Documentation**: https://docs.github.com/en/actions
- **Self-Hosted Runners**: https://docs.github.com/en/actions/hosting-your-own-runners
- **Runner Releases**: https://github.com/actions/runner/releases

**Questions or Issues?**
- Check runner logs: `sudo journalctl -u actions.runner.* -n 100`
- Review workflow runs in GitHub Actions tab
- Verify runner connectivity in Settings → Actions → Runners

---

**Setup Complete!** 🎉 Your host-based runner is ready for production deployments.


# Part 2: Maintenance


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
# Follow Part 1 (Setup) of this page from the beginning

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
- **Setup**: [Part 1](#part-1-setup) of this page
- **GitHub setup**: [CI-CD.md](CI-CD.md#github-setup)

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
