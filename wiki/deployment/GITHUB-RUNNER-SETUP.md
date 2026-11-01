# GitHub Self-Hosted Runner Setup Guide

> **✅ MIGRATED**: This guide reflects the **host-based runner** architecture (migrated 2026-02-02)

This guide walks you through setting up a self-hosted GitHub Actions runner on your production VPS for automated deployments.

## 🏗️ Architecture Overview

```
GitHub Repository (Push to main)
    ↓
[GitHub-Hosted Runner] - Build & Test
    ├── Build .NET application
    ├── Run tests
    ├── Build Docker image
    ├── Push to ghcr.io
    └── Trigger deploy job
           ↓
[Self-Hosted Runner on VPS (HOST-BASED)] - Deploy Only
    ├── Runs directly on VPS host (ubuntu user)
    ├── Native Docker access (no socket mounting)
    ├── Direct localhost access (no network complexity)
    ├── Pull latest image from ghcr.io
    ├── Update docker-compose services
    ├── Rolling restart with health checks
    └── Verify deployment
```

**Benefits:**
- ✅ No SSH keys needed in GitHub secrets
- ✅ Faster deployments (direct Docker access, no container overhead)
- ✅ More secure (no remote access to VPS)
- ✅ Free builds on GitHub runners
- ✅ Production credentials stay on VPS
- ✅ Direct localhost access (simplified health checks)
- ✅ Native file permissions (no UID mapping)

---

## Part 1: GitHub Repository Setup

### Step 1: Generate Runner Registration Token

**Option A: Using GitHub Personal Access Token (PAT) - Recommended for Single Repo**

1. Go to GitHub → Your Profile → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click **Generate new token (classic)**
3. Token settings:
   - **Note**: `StarterKit Self-Hosted Runner`
   - **Expiration**: 90 days (set calendar reminder to rotate)
   - **Scopes**: Check the following:
     - ✅ `repo` (Full control of private repositories)
     - ✅ `workflow` (Update GitHub Action workflows)
     - ✅ `admin:repo_hook` (Full control of repository hooks)
4. Click **Generate token**
5. **IMPORTANT**: Copy the token immediately (you won't see it again)
6. Save it securely - you'll need it for VPS setup

**Option B: Using GitHub App - Recommended for Multiple Repos**

1. Go to your Organization → Settings → GitHub Apps → New GitHub App
2. Configure:
   - **Name**: `StarterKit Runner App`
   - **Homepage URL**: Your repository URL
   - **Webhook**: Uncheck "Active"
   - **Permissions**:
     - Repository permissions:
       - Actions: Read & Write
       - Administration: Read & Write
       - Metadata: Read-only
3. Click **Create GitHub App**
4. Install the app to your repository
5. Generate private key and save it

### Step 2: Configure Repository Settings

1. Go to your repository: `https://github.com/YOUR_USERNAME/StarterKit`
2. Click **Settings** → **Actions** → **Runners**
3. You should see a "New self-hosted runner" button
4. Note the runner registration command (we'll use parts of this on VPS)

### Step 3: Update GitHub Secrets

**Remove Old SSH-Based Secrets** (No Longer Needed):
1. Go to **Settings** → **Secrets and variables** → **Actions**
2. Remove these secrets:
   - `VPS_SSH_KEY` ❌
   - `VPS_HOST` ❌
   - `VPS_USER` ❌

**Add New Runner-Related Secrets**:

| Secret Name | Description | Example Value |
|-------------|-------------|---------------|
| `GITHUB_RUNNER_TOKEN` | PAT token generated in Step 1 | `ghp_xxxxxxxxxxxx` |
| `PRODUCTION_URL` | Your production API URL | `https://api.example.com` |
| `DOCKER_REGISTRY` | Container registry | `ghcr.io` |

**Keep Existing Secrets**:
- `GITHUB_TOKEN` - Automatically provided by GitHub Actions ✅
- Database credentials (if any) ✅

### Step 4: Configure Runner Labels

When registering the runner on VPS, use these labels:
- `self-hosted` (automatic)
- `linux` (automatic)
- `x64` (automatic)
- `deploy-only` (custom - important!)
- `vps` (custom - for organization)

---

## Part 2: GitHub Workflow Configuration

### Step 5: Understand the Hybrid Workflow

The new workflow file `.github/workflows/hybrid-ci-cd.yml` has two main jobs:

**Job 1: Build & Push (GitHub-Hosted)**
```yaml
build-and-push:
  runs-on: ubuntu-latest  # ← Runs on GitHub's servers
  steps:
    - Build .NET application
    - Run tests
    - Build Docker image
    - Push to ghcr.io
```

**Job 2: Deploy (Self-Hosted)**
```yaml
deploy:
  runs-on: [self-hosted, deploy-only]  # ← Runs on your VPS
  needs: build-and-push
  steps:
    - Pull Docker image
    - Update services
    - Health checks
```

### Step 6: Workflow Triggers

The workflow runs automatically on:
- ✅ Push to `main` branch
- ✅ Push to `develop` branch (build only, no deploy)
- ✅ Manual trigger via `workflow_dispatch`

---

## Part 3: Security Best Practices

### Required Actions

1. **Enable Branch Protection for `main`**:
   - Settings → Branches → Add rule
   - Branch name pattern: `main`
   - ✅ Require pull request reviews (1 approval)
   - ✅ Require status checks (build-and-test)
   - ✅ Include administrators

2. **Enable Required Workflows**:
   - Settings → Actions → Required workflows
   - Require `hybrid-ci-cd` to pass before merging

3. **Enable Deployment Protection**:
   - Settings → Environments → `production`
   - ✅ Required reviewers (add yourself)
   - ✅ Wait timer: 0 minutes (or add delay)
   - ✅ Deployment branches: `main` only

4. **Runner Security**:
   - ⚠️ NEVER use `runs-on: self-hosted` alone (too broad)
   - ✅ ALWAYS use specific labels: `[self-hosted, deploy-only]`
   - ✅ This prevents arbitrary code execution on your VPS

### Token Security

**PAT Rotation Schedule**:
- Set expiration to 90 days
- Add calendar reminder 1 week before expiration
- Rotation process:
  1. Generate new PAT
  2. Update runner `.env` file on VPS
  3. Restart runner container
  4. Delete old token from GitHub

**What NOT to Do**:
- ❌ Don't commit PAT to repository
- ❌ Don't use PAT with `admin:org` scope (too powerful)
- ❌ Don't share PAT between multiple runners
- ❌ Don't set PAT expiration to "No expiration"

---

## Part 4: Monitoring & Maintenance

### Monitor Runner Health

**GitHub UI**:
1. Repository → Settings → Actions → Runners
2. Check runner status:
   - 🟢 **Idle** - Healthy, waiting for jobs
   - 🔵 **Active** - Currently running a job
   - 🔴 **Offline** - Not connected (investigate!)

**Expected Behavior**:
- Runner should show "Idle" when not deploying
- Runner should auto-reconnect after VPS reboot
- Runner should pick up jobs within 5-10 seconds

### Troubleshooting

**Runner Shows Offline**:
```bash
# SSH to VPS
ssh ubuntu@YOUR_SERVER_IP

# Check runner service status
SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
sudo systemctl status $SERVICE_NAME

# View runner logs
sudo journalctl -u $SERVICE_NAME -f

# Restart runner
sudo systemctl restart $SERVICE_NAME
```

**Jobs Not Picking Up**:
- Verify runner labels match workflow (`deploy-only`)
- Check runner token hasn't expired
- Ensure runner has network access to GitHub (port 443)
- Check runner service is active: `sudo systemctl status actions.runner.*`

**Deployment Fails**:
- Check runner has Docker access: `docker ps`
- Verify runner user has permissions to `/opt/starterkit`
- Review job logs in GitHub Actions tab
- Check health checks: `cd /opt/starterkit && ./deployment/scripts/health-check.sh`

---

## Part 5: Regular Maintenance Tasks

### Weekly
- [ ] Check runner status in GitHub UI
- [ ] Review deployment logs for errors

### Monthly
- [ ] Update runner binary:
  ```bash
  # Check for updates: https://github.com/actions/runner/releases
  cd ~/actions-runner
  ./config.sh --version

  # If update available, see VPS-RUNNER-SETUP.md Part 9
  ```
- [ ] Clean old logs:
  ```bash
  sudo journalctl --vacuum-time=30d
  ```

### Quarterly (Every 90 Days)
- [ ] Rotate PAT token (see Token Security section)
- [ ] Review runner logs disk usage
- [ ] Update runner configuration if needed

### After VPS Reboot
- Runner should auto-start via systemd service
- Verify in GitHub UI that runner reconnects within 2 minutes
- If not, manually restart:
  ```bash
  SERVICE_NAME="actions.runner.ubuntu-$(hostname).service"
  sudo systemctl start $SERVICE_NAME
  ```

---

## Part 6: Rollback Plan

If self-hosted runner fails and you need to deploy urgently:

1. **Temporary SSH-based deployment**:
   ```bash
   # On your local machine
   ssh your-user@vps-ip
   cd /opt/starterkit
   ./deployment/deploy.sh
   ```

2. **Re-enable old workflow**:
   - Rename `.github/workflows/docker-build-deploy.yml.disabled` back to `.yml`
   - Re-add SSH secrets to GitHub
   - Push change to trigger old workflow

3. **Fix runner issues** (after urgent deploy):
   - Review logs
   - Fix configuration
   - Test runner pickup

4. **Switch back to hybrid**:
   - Disable old workflow
   - Verify hybrid workflow works
   - Remove SSH secrets again

---

## Next Steps

✅ After completing this guide, proceed to:
1. **VPS Setup Guide** → `VPS-RUNNER-SETUP.md`
2. Configure runner on your VPS
3. Test deployment workflow
4. Monitor first few deployments

---

## Support & Resources

- **GitHub Actions Documentation**: https://docs.github.com/en/actions
- **Self-Hosted Runners**: https://docs.github.com/en/actions/hosting-your-own-runners
- **Runner Docker Images**: https://github.com/myoung34/docker-github-actions-runner

**Questions or Issues?**
- Check runner logs: `docker compose -f docker-compose.runner.yml logs`
- Review workflow runs in GitHub Actions tab
- Verify runner connectivity in Settings → Actions → Runners
