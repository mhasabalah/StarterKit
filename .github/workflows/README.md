# GitHub Actions Workflows

This directory contains the automated CI/CD workflows for StarterKit.

## 🚀 Active Workflows

### 1. Hybrid CI/CD (`hybrid-ci-cd.yml`) ⭐ PRIMARY

**Trigger:** Push to `master`/`develop` branches, Manual dispatch
**Purpose:** Main production deployment pipeline
**Architecture:**
- **Build & Test**: GitHub-hosted runner (ubuntu-latest)
- **Docker Build & Push**: GitHub-hosted runner
- **Deploy**: Self-hosted runner on VPS (host-based)

**Features:**
- ✅ Automated testing before deployment
- ✅ Docker image build and push to GHCR
- ✅ Zero-downtime rolling updates
- ✅ Comprehensive health checks
- ✅ Automatic database backups before deployment
- ✅ Deployment summaries

**Usage:**
```bash
# Automatic: Push to master
git push origin master

# Manual: Use GitHub UI
# Actions → Hybrid CI/CD → Run workflow
```

### 2. Health Monitoring (`health-monitoring.yml`)

**Trigger:** Every 6 hours (00:00, 06:00, 12:00, 18:00 UTC), Manual dispatch
**Purpose:** Monitor production health and resources
**Runs on:**
- Health checks: GitHub-hosted runner
- Resource monitoring: Self-hosted runner

**Checks:**
- ✅ API /health endpoint
- ✅ API /alive endpoint
- ✅ Response time monitoring
- ✅ Server disk usage
- ✅ Server memory usage
- ✅ Docker container status

**Alerts:**
- 🚨 Disk usage > 85%
- ⚠️ Disk usage > 75%
- 🚨 Available memory < 5%
- ⚠️ Available memory < 15%
- 🚨 API container not running

**Schedule:**
```
Runs every 6 hours:
- 00:00 UTC (03:00 Riyadh time)
- 06:00 UTC (09:00 Riyadh time)
- 12:00 UTC (15:00 Riyadh time)
- 18:00 UTC (21:00 Riyadh time)
```

### 3. Test Runner (`test-runner.yml`)

**Trigger:** Manual dispatch only
**Purpose:** Test self-hosted runner configuration
**Runs on:** Self-hosted runner

**Test Types:**
1. **Basic**: System information, disk, memory
2. **Docker**: Docker access, containers, images
3. **Deployment Dry Run**: Full deployment verification

**Usage:**
```bash
# GitHub UI: Actions → Test Self-Hosted Runner → Run workflow
# Select test type: basic | docker | deployment-dry-run
```

### 4. Rollback (`rollback.yml`) 🆘 EMERGENCY

**Trigger:** Manual dispatch only (requires confirmation)
**Purpose:** Emergency rollback to previous database state
**Runs on:** Self-hosted runner

**Safety Features:**
- ⚠️ Requires typing "ROLLBACK" to confirm
- 💾 Creates safety backup before rollback
- 🔍 Lists available backups
- 🏥 Health verification after rollback

**Usage:**
```bash
# GitHub UI: Actions → Rollback Production Deployment → Run workflow
# ⚠️ Type "ROLLBACK" in confirmation field
# Optional: Specify backup file name (defaults to latest)
```

## ❌ Removed Workflows (2026-02-04)

The following workflows were **removed** as obsolete/redundant:

### 1. `ci-cd-production.yml` (REMOVED)
- **Reason**: Obsolete SSH-based deployment
- **Replaced by**: `hybrid-ci-cd.yml` (self-hosted runner)
- **Issue**: Used SSH instead of native self-hosted runner

### 2. `docker-build-deploy.yml` (REMOVED)
- **Reason**: Completely redundant with `hybrid-ci-cd.yml`
- **Replaced by**: `hybrid-ci-cd.yml`
- **Issue**: Duplicate functionality

### 3. `manual-deploy.yml` (REMOVED)
- **Reason**: Redundant - same functionality available in hybrid workflow
- **Replaced by**: `hybrid-ci-cd.yml` with workflow_dispatch
- **Issue**: Manual deployment can be done via hybrid workflow

## 📊 Workflow Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    GitHub Repository                    │
└─────────────────────────────────────────────────────────┘
                          │
                          ├─── Push to master
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│              Hybrid CI/CD Workflow                      │
├─────────────────────────────────────────────────────────┤
│  1. Build & Test (GitHub-hosted)                        │
│     ├── Restore dependencies                            │
│     ├── Build solution                                  │
│     └── Run tests                                       │
│                                                         │
│  2. Build & Push Docker (GitHub-hosted)                 │
│     ├── Build Docker image                              │
│     └── Push to GHCR                                    │
│                                                         │
│  3. Deploy (Self-hosted VPS)                            │
│     ├── Backup database                                 │
│     ├── Pull latest image                               │
│     ├── Zero-downtime update                            │
│     └── Health verification                             │
└─────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────┐
│         Production VPS (YOUR_SERVER_IP)                  │
│         https://api.example.com                            │
└─────────────────────────────────────────────────────────┘
```

## 🔐 Required Secrets

These secrets must be configured in GitHub repository settings:

| Secret | Purpose | Used By |
|--------|---------|---------|
| `GITHUB_TOKEN` | Automatic (GitHub provides) | All workflows |
| (None required!) | Self-hosted runner handles deployment | Deploy jobs |

**Note**: The self-hosted runner runs directly on the VPS, so no SSH keys or credentials are needed!

## 📝 Workflow Summary

| Workflow | Trigger | Frequency | Runner | Purpose |
|----------|---------|-----------|--------|---------|
| Hybrid CI/CD | Push to master | On-demand | Hybrid | Main deployment |
| Health Monitoring | Schedule | Every 6 hours | Hybrid | Monitor production |
| Test Runner | Manual | On-demand | Self-hosted | Test runner setup |
| Rollback | Manual | Emergency | Self-hosted | Emergency rollback |

## 🎯 Quick Actions

### Deploy to Production
```bash
git push origin master
# Or use GitHub UI: Actions → Hybrid CI/CD → Run workflow
```

### Check Production Health
```bash
# Automatic every 6 hours
# Or manual: Actions → Health Check & Monitoring → Run workflow
```

### Test Runner Configuration
```bash
# GitHub UI: Actions → Test Self-Hosted Runner → Run workflow
```

### Emergency Rollback
```bash
# GitHub UI: Actions → Rollback Production → Run workflow
# ⚠️ Type "ROLLBACK" to confirm
```

## 📚 Related Documentation

- [VPS Runner Setup](../../wiki/deployment/VPS-RUNNER-SETUP.md) - Self-hosted runner configuration
- [Production Deployment](../../wiki/deployment/Production-Deployment.md) - Production setup guide
- [Deployment Index](../../wiki/deployment/INDEX.md) - Complete deployment documentation

## 🔄 Recent Changes

**2026-02-04**: Workflow cleanup
- ❌ Removed `ci-cd-production.yml` (obsolete SSH-based)
- ❌ Removed `docker-build-deploy.yml` (redundant)
- ❌ Removed `manual-deploy.yml` (redundant)
- ✅ Updated `health-monitoring.yml` (6-hour schedule, improved checks)
- ✅ Updated `rollback.yml` (self-hosted runner, better safety)
- ✅ Kept `hybrid-ci-cd.yml` as primary workflow
- ✅ Kept `test-runner.yml` for testing

## 💡 Best Practices

1. **Always use Hybrid CI/CD for deployments** - It's the tested, reliable workflow
2. **Monitor health checks** - Review failures immediately
3. **Test runner before major changes** - Run test-runner workflow
4. **Keep rollback ready** - Know how to use it in emergencies
5. **Review workflow runs** - Check Actions tab regularly

## 🆘 Troubleshooting

### Deployment Failed
1. Check workflow logs in Actions tab
2. Verify self-hosted runner is online: `systemctl status actions.runner.*`
3. Check VPS resources: disk space, memory
4. Review container logs: `docker compose logs -f api`

### Health Check Failed
1. Check if API is responding: `curl http://localhost:5000/health`
2. Check container status: `docker compose ps`
3. Review logs: `docker compose logs api`
4. Check nginx: `sudo nginx -t && sudo systemctl status nginx`

### Self-Hosted Runner Issues
1. Check runner service: `systemctl status actions.runner.*`
2. Check runner logs: `journalctl -u actions.runner.* -f`
3. Restart runner: `sudo systemctl restart actions.runner.*`
4. See: [Runner Maintenance Guide](../../wiki/deployment/RUNNER-MAINTENANCE.md)

---

**Last Updated**: 2026-02-04
**Maintainer**: GitHub Actions Team
