# Self-Hosted GitHub Actions Runner - Setup Complete! 🎉

> **✅ MIGRATED**: This guide reflects the **host-based runner** architecture (migrated 2026-02-02)

## What We've Built

You now have a **hybrid CI/CD architecture** with:
- ✅ **Build & Test** jobs running on GitHub-hosted runners (free, fast, secure)
- ✅ **Deploy** job running on your VPS via **host-based** self-hosted runner (no SSH needed!)
- ✅ Direct localhost access for health checks (simplified networking)
- ✅ Native Docker access without container overhead
- ✅ Complete documentation for setup and maintenance
- ✅ Systemd service management for reliability
- ✅ Test workflows to verify everything works

## 📁 Key Documentation

### Documentation
- **`wiki/deployment/GITHUB-RUNNER-SETUP.md`** - Complete GitHub setup guide
- **`wiki/deployment/VPS-RUNNER-SETUP.md`** - Complete VPS host-based installation guide
- **`wiki/deployment/RUNNER-MAINTENANCE.md`** - Runner maintenance guide

### Runner Installation
- **`~/actions-runner/`** - Runner binaries on VPS host (installed directly, not in container)
- **Systemd service**: `actions.runner.ubuntu-<hostname>.service`

### GitHub Workflows
- **`.github/workflows/hybrid-ci-cd.yml`** - Main deployment workflow
- **Works unchanged** - Same labels (`self-hosted`, `deploy-only`) work for host-based runner

## 🚀 Quick Start Guide

### Step 1: GitHub Configuration (5 minutes)

1. **Generate Personal Access Token (PAT)**:
   - Go to: GitHub → Settings → Developer settings → Personal access tokens
   - Click "Generate new token (classic)"
   - Scopes: `repo`, `workflow`, `admin:repo_hook`
   - Expiration: 90 days (set reminder!)
   - Copy token (you won't see it again!)

2. **Update Repository Secrets**:
   - Go to: Repository → Settings → Secrets and variables → Actions
   - **Remove old secrets**: `VPS_SSH_KEY`, `VPS_HOST`, `VPS_USER` (no longer needed!)
   - **Add** `PRODUCTION_URL` (optional): `https://api.example.com`

3. **Configure Branch Protection**:
   - Settings → Branches → Add rule for `main`
   - ✅ Require pull request reviews
   - ✅ Require status checks

**Full details**: See `deployment/GITHUB-RUNNER-SETUP.md`

---

### Step 2: VPS Installation (15-20 minutes)

SSH into your VPS and run these commands:

```bash
# 1. Create runner directory
mkdir -p ~/actions-runner && cd ~/actions-runner

# 2. Download GitHub Actions runner
RUNNER_VERSION="2.311.0"
curl -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz -L \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# 3. Extract runner
tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# 4. Configure runner
./config.sh \
  --url https://github.com/your-org/StarterKit \
  --token YOUR_REGISTRATION_TOKEN_HERE \
  --name starterkit-vps-host-runner \
  --labels self-hosted,deploy-only,vps,production \
  --work _work \
  --replace

# 5. Install as systemd service
sudo ./svc.sh install ubuntu

# 6. Start runner service
SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
sudo systemctl start $SERVICE_NAME
sudo systemctl enable $SERVICE_NAME

# 7. Check status
sudo systemctl status $SERVICE_NAME
sudo journalctl -u $SERVICE_NAME -f

# 8. Configure sudo access for Nginx
sudo visudo -f /etc/sudoers.d/github-runner
# Add line: ubuntu ALL=(ALL) NOPASSWD: /usr/sbin/nginx -s reload, /usr/bin/systemctl reload nginx, /usr/bin/systemctl restart nginx
```

**Full details**: See `wiki/deployment/VPS-RUNNER-SETUP.md`

---

### Step 3: Verify Runner Connection (2 minutes)

1. **In GitHub**:
   - Go to: Repository → Settings → Actions → Runners
   - You should see: 🟢 **starterkit-vps-host-runner** - Idle

2. **On VPS**:
   ```bash
   SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
   sudo systemctl status $SERVICE_NAME
   sudo journalctl -u $SERVICE_NAME -n 20

   # Test capabilities
   docker ps
   curl http://localhost:5000/health
   ls -la /opt/starterkit/
   ```

**Expected**: Runner shows as healthy, connected to GitHub, and all capabilities work

---

### Step 4: Test Runner (5 minutes)

1. **Run test workflow**:
   - Go to: Actions tab → "Test Self-Hosted Runner"
   - Click "Run workflow"
   - Select test type: **deployment-dry-run**
   - Click "Run workflow"

2. **Watch it execute**:
   - Job should run on your VPS runner
   - Should complete successfully with ✅

3. **On VPS, watch logs**:
   ```bash
   SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
   sudo journalctl -u $SERVICE_NAME -f
   ```

**If successful**: You're ready for production deployments! 🎉

---

### Step 5: First Production Deployment (Optional)

**Option A: Automatic (Push to main)**
```bash
git add .
git commit -m "feat: add self-hosted runner infrastructure"
git push origin main
```

**Option B: Manual Trigger**
- Go to: Actions → "Hybrid CI/CD" → Run workflow
- Environment: production
- Click "Run workflow"

**What happens**:
1. ✅ GitHub-hosted runner: Builds .NET app, runs tests, builds Docker image
2. ✅ Pushes image to ghcr.io
3. ✅ Self-hosted runner: Pulls image, deploys to VPS, health checks

---

## 🎯 What Changed from Old Setup

| **Before (SSH)** | **After (Host-Based Runner)** |
|------------------|--------------------------------|
| ❌ SSH keys in GitHub Secrets | ✅ No SSH needed |
| ❌ SCP file transfers over network | ✅ Direct Docker access |
| ❌ VPS credentials in GitHub | ✅ Credentials stay on VPS |
| ❌ Complex SSH connection setup | ✅ Simple runner pickup |
| ⏱️ Slower deployments (network) | ⚡ Faster (native performance) |
| ❌ Network complexity for localhost | ✅ Direct localhost access |

---

## 📊 Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ GitHub Repository (main branch)                             │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Job 1: Build & Test (GitHub-Hosted Runner)                  │
│ • Restore dependencies                                       │
│ • Build .NET solution                                        │
│ • Run tests                                                  │
│ • Build Docker image                                         │
│ • Push to ghcr.io                                           │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Job 2: Deploy (Host-Based Runner on VPS)                    │
│ • Runs as ubuntu user on VPS host (no container)            │
│ • Native Docker access, direct localhost access             │
│ • Checkout deployment scripts                               │
│ • Login to container registry                               │
│ • Create database backup                                    │
│ • Pull latest Docker image                                  │
│ • Rolling update containers                                 │
│ • Health checks (direct localhost:5000)                     │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔐 Security Benefits

1. **No SSH Keys**: Runner is already on VPS, no remote access needed
2. **No Credentials in GitHub**: VPS secrets never leave the server
3. **Isolated Execution**: Runner runs in Docker with limited permissions
4. **Token Rotation**: PAT can be rotated without workflow changes
5. **Audit Trail**: All runner activity logged

---

## 🔧 Management Commands

```bash
SERVICE="actions.runner.ubuntu-$(hostname).service"

# Daily operations
sudo systemctl status $SERVICE       # Check runner status
sudo systemctl restart $SERVICE      # Restart runner
sudo journalctl -u $SERVICE -f       # View logs (live)
sudo journalctl -u $SERVICE -n 100   # View logs (last 100 lines)

# Health checks
cd /opt/starterkit && ./deployment/scripts/health-check.sh
docker ps
curl http://localhost:5000/health

# Maintenance
sudo systemctl enable $SERVICE       # Enable auto-start
sudo systemctl disable $SERVICE      # Disable auto-start
sudo journalctl --vacuum-time=30d    # Clean old logs
```

---

## 📚 Documentation Reference

| Document | Purpose |
|----------|---------|
| `wiki/deployment/GITHUB-RUNNER-SETUP.md` | GitHub configuration, secrets, security |
| `wiki/deployment/VPS-RUNNER-SETUP.md` | Complete VPS host-based installation guide |
| `wiki/deployment/RUNNER-MAINTENANCE.md` | Runner maintenance and troubleshooting |
| `.github/workflows/hybrid-ci-cd.yml` | Main deployment workflow |

---

## 🐛 Troubleshooting

### Runner Shows Offline in GitHub

```bash
SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
sudo systemctl status $SERVICE_NAME
sudo journalctl -u $SERVICE_NAME -n 100
sudo systemctl restart $SERVICE_NAME
```

### Jobs Not Picking Up

1. Verify labels in workflow: `runs-on: [self-hosted, deploy-only]`
2. Check runner labels in GitHub UI
3. Verify PAT token hasn't expired
4. Check runner service: `sudo systemctl status actions.runner.*`

### Deployment Fails

```bash
# Check runner logs
SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
sudo journalctl -u $SERVICE_NAME -n 100

# Verify Docker access
docker ps

# Check workspace
ls -la /opt/starterkit/

# Test health checks
cd /opt/starterkit && ./deployment/scripts/health-check.sh
```

**More help**: See `wiki/deployment/VPS-RUNNER-SETUP.md` → Part 7: Troubleshooting

---

## 🔄 Regular Maintenance

### Weekly
- [ ] Check runner status in GitHub UI
- [ ] Review deployment logs

### Monthly
- [ ] Check for runner updates: `cd ~/actions-runner && ./config.sh --version`
- [ ] Clean old logs: `sudo journalctl --vacuum-time=30d`

### Quarterly (Every 90 Days)
- [ ] Rotate GitHub PAT token
- [ ] Update `runner.env` with new token
- [ ] Restart runner

---

## ✅ Success Checklist

Before considering setup complete:

- [ ] Runner shows 🟢 Idle in GitHub Settings → Actions → Runners
- [ ] Test deployment completes successfully
- [ ] `sudo systemctl status actions.runner.*` shows active (running)
- [ ] Runner logs show "Listening for Jobs"
- [ ] Docker access works: `docker ps`
- [ ] Localhost access works: `curl http://localhost:5000/health`
- [ ] Filesystem access works: `ls -la /opt/starterkit/`
- [ ] Old SSH secrets removed from GitHub
- [ ] PAT token expiration reminder set (90 days)
- [ ] Systemd service enabled for auto-start
- [ ] Team members notified of new deployment process

---

## 🎉 You're Done!

Your self-hosted runner is now ready for production use!

**Next Steps**:
1. ✅ Push code to `main` branch to trigger automatic deployment
2. ✅ Monitor first deployment in Actions tab
3. ✅ Set up monitoring and alerts (optional)
4. ✅ Update team documentation

**Questions?** Check the comprehensive guides:
- GitHub setup: `wiki/deployment/GITHUB-RUNNER-SETUP.md`
- VPS setup: `wiki/deployment/VPS-RUNNER-SETUP.md`
- Runner maintenance: `wiki/deployment/RUNNER-MAINTENANCE.md`

---

**Setup Status**: ✅ Complete
**Ready for**: Production deployments
**Architecture**: Hybrid (GitHub-hosted + Host-based self-hosted)
**Runner Type**: Host-based (native, not containerized)
**Security**: Enhanced (no SSH, no credentials in GitHub)
**Performance**: Improved (native performance, direct localhost access)

Happy Deploying! 🚀
