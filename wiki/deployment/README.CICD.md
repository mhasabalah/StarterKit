# 🚀 StarterKit CI/CD with GitHub Actions

Complete guide for automated deployment using GitHub Actions.

---

## 📋 Table of Contents

1. [Overview](#overview)
2. [Workflows](#workflows)
3. [Setup Instructions](#setup-instructions)
4. [GitHub Secrets Configuration](#github-secrets-configuration)
5. [Usage Guide](#usage-guide)
6. [Deployment Strategies](#deployment-strategies)
7. [Rollback Procedures](#rollback-procedures)
8. [Monitoring & Alerts](#monitoring--alerts)
9. [Troubleshooting](#troubleshooting)

---

## 🎯 Overview

### CI/CD Architecture

```
GitHub Repository
     ↓
[Push to main/develop]
     ↓
GitHub Actions Workflow
     ├─ Build & Test (.NET)
     ├─ Build Docker Images
     ├─ Security Scan (Trivy)
     ├─ Push to GitHub Container Registry
     └─ Deploy to EC2
         ├─ Backup Database
         ├─ Pull Latest Images
         ├─ Rolling Update (API 1 → API 2)
         ├─ Health Checks
         └─ Cleanup
```

### Key Features

✅ **Automated Testing**: Runs unit and integration tests  
✅ **Docker Build**: Builds and pushes to GitHub Container Registry  
✅ **Security Scanning**: Trivy vulnerability scanner  
✅ **Zero-Downtime Deployment**: Rolling updates  
✅ **Automated Backups**: Database backup before each deployment  
✅ **Health Checks**: Validates deployment success  
✅ **Rollback Capability**: One-click rollback to previous version  
✅ **Monitoring**: Scheduled health checks  

---

## 📦 Workflows

### 1. **CI/CD - Build, Test & Deploy** (`ci-cd-production.yml`)

**Triggers**:
- Push to `main` or `develop` branches
- Pull requests to `main`
- Manual trigger via workflow_dispatch

**Jobs**:
1. **build-and-test**: Build .NET solution and run tests
2. **build-docker**: Build and push Docker images to GHCR
3. **security-scan**: Scan images for vulnerabilities
4. **deploy-production**: Deploy to EC2 with rolling updates
5. **rollback**: Rollback on failure (manual trigger)

**Process**:
```
Code Push → Build → Test → Docker Build → Security Scan → Deploy → Verify
                                               ↓
                                          (If Failed)
                                               ↓
                                           Rollback
```

### 2. **Manual Deploy** (`manual-deploy.yml`)

**Purpose**: On-demand deployment with custom options

**Options**:
- Create backup (yes/no)
- Run migrations (yes/no)
- Restart strategy (rolling/all-at-once)

**Use Cases**:
- Hotfix deployments
- Off-hours deployments
- Configuration-only updates

### 3. **Rollback** (`rollback.yml`)

**Purpose**: Restore production to previous state

**Features**:
- Requires "ROLLBACK" confirmation
- Automatic safety backup before rollback
- Restore from specific backup or latest
- Health verification after rollback

**Use Cases**:
- Critical bugs in production
- Failed deployment recovery
- Data corruption recovery

### 4. **Health Monitoring** (`health-monitoring.yml`)

**Schedule**: Every hour (configurable)

**Checks**:
- API health endpoint
- System resources (CPU, memory, disk)
- Docker container status
- Alert on failures

---

## 🔧 Setup Instructions

### Step 1: Prepare EC2 Instance

Follow the main deployment guide first:
```bash
# Complete steps in README.Production.md
# Ensure your EC2 instance is fully deployed and working
```

### Step 2: Configure GitHub Repository

#### Enable GitHub Container Registry

1. Go to your repository → **Settings** → **Packages**
2. Ensure "Container registry" is enabled
3. Set visibility to "Private" or "Public" as needed

#### Set Up Environments

1. Go to **Settings** → **Environments**
2. Create environment: `production`
3. Add protection rules:
   - ☑️ Required reviewers (optional)
   - ☑️ Wait timer: 5 minutes (optional)
4. Add environment URL: `http://YOUR_EC2_IP`

### Step 3: Create GitHub Secrets

Go to **Settings** → **Secrets and variables** → **Actions**

Add the following secrets:

| Secret Name | Description | How to Get |
|-------------|-------------|------------|
| `EC2_HOST` | EC2 public IP or domain | AWS Console → EC2 → Instance |
| `EC2_USER` | SSH username (usually `ubuntu`) | Default for Ubuntu AMI |
| `EC2_SSH_PRIVATE_KEY` | SSH private key | Your local `.pem` file content |
| `PRODUCTION_URL` | Full production URL | `http://YOUR_EC2_IP` |

#### How to Add SSH Private Key

```bash
# On your local machine
cat your-ec2-key.pem

# Copy the entire output including:
# -----BEGIN RSA PRIVATE KEY-----
# ...
# -----END RSA PRIVATE KEY-----
```

Paste into GitHub Secret `EC2_SSH_PRIVATE_KEY`

### Step 4: Update Docker Compose for GHCR

Edit `docker-compose.yml` and `deployment/docker-compose.production.yml`:

```yaml
services:
  api:
    image: ghcr.io/YOUR_GITHUB_USERNAME/starterkit:latest
    # ... rest of config
```

Replace `YOUR_GITHUB_USERNAME` with your GitHub username/org.

### Step 5: Test the Workflow

1. Create a test branch:
```bash
git checkout -b test-cicd
```

2. Make a small change (add a comment)

3. Commit and push:
```bash
git add .
git commit -m "test: CI/CD setup"
git push origin test-cicd
```

4. Create a Pull Request to `main`

5. Check the **Actions** tab to see the workflow run

6. If all checks pass, merge the PR

7. The main workflow will deploy to production!

---

## 🔐 GitHub Secrets Configuration

### Required Secrets (Minimum)

```bash
# EC2 Access
EC2_HOST=1.2.3.4                    # Your EC2 public IP
EC2_USER=ubuntu                     # SSH username
EC2_SSH_PRIVATE_KEY=-----BEGIN...   # Your .pem file content

# Production URL
PRODUCTION_URL=http://1.2.3.4       # Or https://yourdomain.com
```

### Optional Secrets (Enhancements)

```bash
# Notifications
SLACK_WEBHOOK_URL=https://hooks.slack.com/...
DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...

# Container Registry (if using Docker Hub instead of GHCR)
DOCKERHUB_USERNAME=your_username
DOCKERHUB_TOKEN=your_access_token

# Environment Variables (if not using .env.production on server)
POSTGRES_PASSWORD=your_db_password
REDIS_PASSWORD=your_redis_password
JWT_SECRET_KEY=your_jwt_secret
```

### How to Add Secrets

1. Go to repository **Settings**
2. Click **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Enter name and value
5. Click **Add secret**

---

## 📖 Usage Guide

### Automatic Deployment (Main Branch)

**Trigger**: Push to `main` branch

```bash
# Make changes
git add .
git commit -m "feat: add new feature"
git push origin main
```

**What happens**:
1. ✅ Tests run
2. 🐳 Docker images built
3. 🔒 Security scan
4. 💾 Database backup
5. 🚀 Rolling deployment
6. 🏥 Health checks
7. ✅ Success or rollback

**Time**: ~10-15 minutes

### Manual Deployment

**Use when**: You need custom deployment options

1. Go to **Actions** tab
2. Select **Manual Deploy to Production**
3. Click **Run workflow**
4. Choose options:
   - Backup database: ☑️ Yes
   - Run migrations: ☑️ Yes
   - Strategy: Rolling
5. Click **Run workflow**

### Rollback Deployment

**Use when**: Production has critical issues

1. Go to **Actions** tab
2. Select **Rollback Production Deployment**
3. Click **Run workflow**
4. Enter confirmation: `ROLLBACK`
5. (Optional) Specify backup file
6. Click **Run workflow**

**Time**: ~5 minutes

---

## 🔄 Deployment Strategies

### Rolling Update (Recommended)

**Process**:
```
1. Update API instance 1 → Wait 10s → Health check
2. Update API instance 2 → Wait 10s → Health check
3. Update other services
```

**Pros**:
- ✅ Zero downtime
- ✅ Partial rollback possible
- ✅ Gradual traffic shift

**Cons**:
- ⏱️ Slower (adds ~30 seconds)

**Use for**: Production deployments

### All-at-Once Update

**Process**:
```
1. Stop all services
2. Update all services
3. Start all services
```

**Pros**:
- ⚡ Faster
- 🔄 Simple

**Cons**:
- ⚠️ Downtime (~30-60 seconds)
- ❌ All-or-nothing

**Use for**: Maintenance windows, staging

---

## 🔙 Rollback Procedures

### Automatic Rollback

Triggered automatically when:
- Health check fails after deployment
- Deployment script returns error
- Critical failure detected

**Process**:
```
1. Detect failure
2. Restore from pre-deployment backup
3. Restart services
4. Verify health
5. Notify team
```

### Manual Rollback

**Scenario 1: Rollback to Latest Backup**

```yaml
# In GitHub Actions UI:
Backup file: (leave empty)
Confirmation: ROLLBACK
```

**Scenario 2: Rollback to Specific Backup**

```yaml
# In GitHub Actions UI:
Backup file: pre_deploy_20260122_120000.sql.gz
Confirmation: ROLLBACK
```

**Scenario 3: Emergency SSH Rollback**

```bash
# SSH to EC2
ssh -i your-key.pem ubuntu@YOUR_EC2_IP

cd /opt/starterkit

# List backups
ls -lh /backup/

# Restore specific backup
./deployment/scripts/restore-db.sh /backup/pre_deploy_20260122_120000.sql.gz

# Restart services
docker compose -f docker-compose.yml -f deployment/docker-compose.production.yml restart
```

---

## 📊 Monitoring & Alerts

### Built-in Monitoring

1. **Scheduled Health Checks**
   - Runs: Every hour
   - Checks: API health, resources
   - Alerts: On failure

2. **Deployment Success/Failure**
   - Notified in Actions tab
   - GitHub notifications

3. **Resource Monitoring**
   - Disk usage > 80%
   - Memory available < 10%
   - Container health

### Adding Custom Notifications

#### Slack Integration

Edit workflow files to add:

```yaml
- name: 🔔 Notify Slack
  if: always()
  uses: slackapi/slack-github-action@v1
  with:
    webhook-url: ${{ secrets.SLACK_WEBHOOK_URL }}
    payload: |
      {
        "text": "Deployment ${{ job.status }}: ${{ github.repository }}"
      }
```

#### Discord Integration

```yaml
- name: 🔔 Notify Discord
  if: always()
  run: |
    curl -X POST -H 'Content-type: application/json' \
      --data "{\"content\":\"Deployment ${{ job.status }}\"}" \
      ${{ secrets.DISCORD_WEBHOOK_URL }}
```

#### Email Notification

```yaml
- name: 📧 Send Email
  if: always()
  uses: dawidd6/action-send-mail@v3
  with:
    server_address: smtp.gmail.com
    server_port: 465
    username: ${{ secrets.EMAIL_USERNAME }}
    password: ${{ secrets.EMAIL_PASSWORD }}
    subject: Deployment ${{ job.status }}
    body: Deployment completed with status ${{ job.status }}
    to: team@example.com
```

---

## 🔍 Troubleshooting

### Common Issues

#### 1. SSH Connection Failed

**Error**: `Permission denied (publickey)`

**Fix**:
```bash
# Verify SSH key format in secret
# Should include:
-----BEGIN RSA PRIVATE KEY-----
...
-----END RSA PRIVATE KEY-----

# Test SSH locally first
ssh -i your-key.pem ubuntu@YOUR_EC2_IP
```

#### 2. Docker Login Failed

**Error**: `denied: permission_denied`

**Fix**:
- Ensure GITHUB_TOKEN has package write permission
- Check repository package settings
- Verify image name matches repository

#### 3. Health Check Timeout

**Error**: `Health check failed after 10 attempts`

**Fix**:
```bash
# SSH to EC2
ssh -i your-key.pem ubuntu@YOUR_EC2_IP

# Check logs
cd /opt/starterkit
docker compose logs api api2

# Check containers
docker ps

# Manual health check
./deployment/scripts/health-check.sh
```

#### 4. Deployment Hangs

**Fix**:
- Check GitHub Actions logs
- SSH to EC2 and check processes
- Look for locked files or hung containers

#### 5. Rollback Failed

**Fix**:
```bash
# SSH to EC2
ssh -i your-key.pem ubuntu@YOUR_EC2_IP

cd /opt/starterkit

# List all backups
ls -lh /backup/

# Manual restore
./deployment/scripts/restore-db.sh /backup/SPECIFIC_BACKUP.sql.gz

# Restart everything
docker compose -f docker-compose.yml -f deployment/docker-compose.production.yml down
docker compose -f docker-compose.yml -f deployment/docker-compose.production.yml up -d
```

### Debug Mode

Enable verbose logging in workflow:

```yaml
- name: 🐛 Debug Deploy
  env:
    ACTIONS_STEP_DEBUG: true
  run: |
    set -x  # Enable bash debug
    # ... your deployment commands
```

---

## 📝 Best Practices

### Before Every Deployment

- [ ] Review code changes
- [ ] Run tests locally
- [ ] Check staging environment (if available)
- [ ] Verify backup system is working
- [ ] Schedule deployment during low-traffic hours
- [ ] Notify team

### After Every Deployment

- [ ] Monitor health checks (first 15 minutes)
- [ ] Review application logs
- [ ] Check monitoring dashboards (Grafana)
- [ ] Verify key features working
- [ ] Document any issues

### Security

- [ ] Rotate secrets regularly (every 90 days)
- [ ] Use environment protection rules
- [ ] Require approvals for production
- [ ] Enable branch protection on `main`
- [ ] Review workflow logs for secrets leaks
- [ ] Keep workflows updated

---

## 🚀 Advanced Configuration

### Multi-Environment Setup

Create separate workflows for:
- `staging.yml` → Deploy to staging server
- `production.yml` → Deploy to production
- `dr.yml` → Deploy to disaster recovery

### Blue-Green Deployment

Modify workflow to:
1. Deploy to "green" environment
2. Test green environment
3. Switch traffic to green
4. Keep blue as backup

### Canary Deployment

Update Nginx config to:
1. Route 10% traffic to new version
2. Monitor metrics
3. Gradually increase to 100%

---

## 📞 Support

### Workflow Issues
- Check **Actions** tab for logs
- Review workflow YAML files
- Test SSH connection manually

### Deployment Issues
- SSH to EC2 for direct debugging
- Check `docker compose logs`
- Run `./deployment/scripts/health-check.sh`

### Documentation
- Main guide: `README.Production.md`
- Quick reference: `deployment/QUICKSTART.md`
- Deployment checklist: `deployment/DEPLOYMENT-CHECKLIST.md`

---

## 📚 Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Docker Build Push Action](https://github.com/docker/build-push-action)
- [SSH Action](https://github.com/webfactory/ssh-agent)
- [GitHub Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)

---

**CI/CD Setup Complete!** 🎉

Your automated deployment pipeline is ready. Push to `main` and watch your code automatically deploy to production!
