# VPS Self-Hosted Runner Setup Guide (Host-Based)

> **✅ MIGRATED**: This guide reflects the **host-based runner** architecture (migrated 2026-02-02)

This guide walks you through installing and configuring the GitHub Actions self-hosted runner **directly on your production VPS host**, not in Docker.

## Prerequisites

✅ Before starting, ensure you have:
- Root or sudo access to your VPS
- Docker and Docker Compose installed on the host
- At least 2GB free disk space
- Network access to GitHub (port 443)
- Completed **GitHub Repository Setup** (see `GITHUB-RUNNER-SETUP.md`)
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
