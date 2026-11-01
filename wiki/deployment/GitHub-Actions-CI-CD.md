# GitHub Actions CI/CD Setup

Complete guide for setting up GitHub Actions CI/CD pipeline for StarterKit.

---

## Overview

**Pipeline Features**:
- ✅ Automated build on push to `master`
- ✅ Run tests and linting
- ✅ Build Docker images
- ✅ Push to container registry (GitHub Container Registry)
- ✅ Deploy to production server
- ✅ Health check verification
- ✅ Rollback on failure

---

## Prerequisites

1. GitHub repository with StarterKit code
2. Production server (AWS EC2) accessible via SSH
3. GitHub Secrets configured

---

## GitHub Secrets Setup

### Navigate to Repository Settings

1. Go to your GitHub repository
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**

### Required Secrets

Add these secrets:

| Secret Name | Description | Example |
|------------|-------------|---------|
| `SSH_PRIVATE_KEY` | SSH private key for EC2 access | Contents of .pem file |
| `EC2_HOST` | EC2 public IP or DNS | `YOUR_SERVER_IP` |
| `EC2_USER` | SSH username | `ubuntu` |
| `POSTGRES_PASSWORD` | Database password | `your_secure_password` |
| `REDIS_PASSWORD` | Redis password | `your_redis_password` |
| `JWT_SECRET` | JWT signing key | `your_jwt_secret_key` |
| `SEQ_ADMIN_PASSWORD_HASH` | Seq admin password hash | Get from Seq docs |

### Generate Secrets

```bash
# Generate strong passwords
openssl rand -base64 32

# Generate JWT secret (min 32 chars)
openssl rand -base64 48

# Get Seq password hash
docker run --rm datalust/seq:latest config hash -p "YourPassword"
```

---

## GitHub Actions Workflow

The workflow file is located at: `.github/workflows/deploy.yml`

### Workflow Structure

```yaml
name: Deploy to Production

on:
  push:
    branches: [ master ]
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - Checkout code
      - Build Docker image
      - Run tests
      - Push to registry

  deploy:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - SSH to server
      - Pull latest code
      - Update secrets
      - Run docker-compose up -d
      - Health check
```

### Trigger Deployment

**Automatic**: Push to `master` branch
```bash
git push origin master
```

**Manual**: GitHub Actions tab → Run workflow

---

## Self-Hosted GitHub Runner (Optional)

For faster deployments and no transfer costs, set up a self-hosted runner on your EC2 instance.

### Install Runner on EC2

```bash
# SSH to EC2
ssh -i key.pem ubuntu@YOUR_SERVER_IP

# Create runner directory
mkdir -p ~/actions-runner && cd ~/actions-runner

# Download runner
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz

# Extract
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz

# Configure (follow on-screen instructions)
./config.sh --url https://github.com/YOUR_USERNAME/starterkit --token YOUR_TOKEN

# Install as service
sudo ./svc.sh install
sudo ./svc.sh start

# Check status
sudo ./svc.sh status
```

### Get Runner Token

1. Go to GitHub repository
2. **Settings** → **Actions** → **Runners**
3. Click **New self-hosted runner**
4. Copy the token from the configuration command

### Update Workflow

Change `runs-on` in `.github/workflows/deploy.yml`:

```yaml
# From:
runs-on: ubuntu-latest

# To:
runs-on: self-hosted
```

---

## Deployment Process

### 1. Developer Pushes Code

```bash
git add .
git commit -m "Add new feature"
git push origin master
```

### 2. GitHub Actions Triggers

- Workflow starts automatically
- View progress: GitHub → Actions tab

### 3. Build Phase

```
✓ Checkout code
✓ Set up .NET SDK
✓ Restore dependencies
✓ Build solution
✓ Run tests
✓ Build Docker image
✓ Push to GitHub Container Registry
```

### 4. Deploy Phase

```
✓ SSH to production server
✓ Pull latest code
✓ Update environment secrets
✓ Stop old containers
✓ Pull new images
✓ Start new containers
✓ Wait for health check
✓ Verify deployment
```

### 5. Health Check

```bash
# Automatic health check in workflow
curl https://api.example.com/health
# Expected: {"status":"Healthy"}
```

### 6. Rollback (if failure)

```bash
# Automatic rollback to previous version
docker-compose down
docker-compose up -d --force-recreate
```

---

## Monitoring Deployments

### View Workflow Run

1. Go to GitHub repository
2. Click **Actions** tab
3. Click on workflow run
4. View logs for each step

### Check Deployment Status

```bash
# SSH to server
ssh -i key.pem ubuntu@YOUR_SERVER_IP

# Check containers
docker-compose ps

# View logs
docker-compose logs -f api

# Check health
curl https://api.example.com/health
```

---

## Troubleshooting

### Deployment Fails at SSH Connection

**Symptom**: "Permission denied" or "Connection refused"

**Solution**:
```bash
# Verify SSH_PRIVATE_KEY secret is correct
# Check EC2_HOST and EC2_USER are correct
# Ensure EC2 Security Group allows SSH from GitHub IPs
```

### Health Check Fails

**Symptom**: Deployment succeeds but health check returns 502/503

**Solution**:
```bash
# SSH to server and check
docker-compose ps
docker-compose logs api

# Restart if needed
docker-compose restart api
```

### Docker Image Build Fails

**Symptom**: Build fails with "no space left on device"

**Solution**:
```bash
# Clean up old images
docker system prune -af

# Check disk space
df -h
```

### Secrets Not Updating

**Symptom**: Application still using old password

**Solution**:
```bash
# Update secrets in GitHub
# Re-run workflow
# Verify secrets applied:
docker-compose exec api env | grep POSTGRES_PASSWORD
```

---

## Best Practices

✅ **Always test locally** before pushing to master
✅ **Use feature branches** for development
✅ **Merge to master** only after review
✅ **Monitor deployments** in Actions tab
✅ **Check health** after each deployment
✅ **Keep secrets secure** - never commit to Git
✅ **Use self-hosted runner** for private projects (faster, no egress costs)
✅ **Enable Dependabot** for dependency updates
✅ **Set up notifications** for failed deployments

---

## Advanced: Blue-Green Deployment

For zero-downtime deployments with instant rollback:

### Setup

```yaml
# In docker-compose.yml, use blue/green services
services:
  api-blue:
    # ... config
  api-green:
    # ... config
```

### Deployment Script

```bash
# Determine current active
CURRENT=$(curl -s http://localhost/version)

# Deploy to inactive
if [ "$CURRENT" == "blue" ]; then
  docker-compose up -d api-green
  NGINX_UPSTREAM=green
else
  docker-compose up -d api-blue
  NGINX_UPSTREAM=blue
fi

# Switch Nginx upstream
# Update nginx config
sudo systemctl reload nginx

# Verify health
curl https://api.example.com/health

# Stop old version
docker-compose stop api-$CURRENT
```

---

## Workflow Examples

### Basic Deployment

```yaml
name: Deploy

on:
  push:
    branches: [ master ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Deploy to Production
        uses: appleboy/ssh-action@master
        with:
          host: ${{ secrets.EC2_HOST }}
          username: ${{ secrets.EC2_USER }}
          key: ${{ secrets.SSH_PRIVATE_KEY }}
          script: |
            cd /opt/starterkit
            git pull origin master
            docker-compose up -d --build
            curl https://api.example.com/health
```

### With Tests

```yaml
name: Deploy with Tests

on:
  push:
    branches: [ master ]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Test
        run: dotnet test

  deploy:
    needs: test
    runs-on: ubuntu-latest
    steps:
      - name: Deploy
        # ... deployment steps
```

---

## Quick Commands

```bash
# Trigger manual deployment
# GitHub UI → Actions → Run workflow

# View deployment logs
# GitHub UI → Actions → Click workflow run

# Check deployment on server
ssh ubuntu@YOUR_SERVER_IP 'docker-compose ps'

# Rollback to previous version
ssh ubuntu@YOUR_SERVER_IP 'cd /opt/starterkit && git checkout HEAD~1 && docker-compose up -d --build'
```

---

**Status**: ✅ CI/CD Ready
**Platform**: GitHub Actions
**Deployment**: Automated on push to master
**Health Checks**: Enabled
**Rollback**: Automatic on failure
