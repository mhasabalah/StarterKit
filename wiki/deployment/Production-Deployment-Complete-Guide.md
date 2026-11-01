# Production Deployment Complete Guide

Complete guide to deploy StarterKit to AWS EC2 with full CI/CD automation.

Start: Fresh EC2 instance  
End: Fully automated GitHub Actions CI/CD

---

## Prerequisites

Before starting:
- AWS Account with EC2 access
- GitHub account with repository access
- Domain name (optional, can use IP initially)
- Local machine with SSH client

---

## Part 1: Launch EC2 Instance

### Step 1.1: Create EC2 Instance

1. Login to AWS Console → EC2 Dashboard
2. Click "Launch Instance"
3. Configure instance:

**Name**: StarterKit-Production

**AMI**: Ubuntu Server 22.04 LTS (64-bit x86)

**Instance Type**: t3.medium
- 2 vCPU
- 4 GB RAM
- Cost: ~$30/month

**Key Pair**: 
- Create new key pair
- Name: starterkit-production
- Type: RSA
- Format: .pem
- Download and save securely

**Network Settings**:
- Create security group: starterkit-sg
- Allow SSH (22) from My IP
- Allow HTTP (80) from Anywhere
- Allow HTTPS (443) from Anywhere

**Storage**: 
- 30 GB gp3 SSD

4. Click "Launch Instance"
5. Wait for instance to be "Running"

### Step 1.2: Allocate Elastic IP

1. EC2 Dashboard → Elastic IPs
2. Click "Allocate Elastic IP address"
3. Click "Allocate"
4. Select the new IP → Actions → Associate Elastic IP address
5. Select your instance → Associate
6. Note the Elastic IP (example: 3.15.123.45)

### Step 1.3: Configure Security Group

1. EC2 Dashboard → Security Groups
2. Select "starterkit-sg"
3. Edit Inbound Rules:

| Type | Port | Source | Description |
|------|------|--------|-------------|
| SSH | 22 | Your IP | SSH access |
| HTTP | 80 | 0.0.0.0/0 | Web traffic |
| HTTPS | 443 | 0.0.0.0/0 | Secure web traffic |

4. Outbound Rules: Leave default (All traffic)

### Step 1.4: Connect to Instance

From your local machine:

```bash
# Change key permissions (first time only)
chmod 400 starterkit-production.pem

# Connect to instance
ssh -i starterkit-production.pem ubuntu@YOUR_ELASTIC_IP
```

You should see Ubuntu welcome message.

---

## Part 2: Install Required Software

All commands run on EC2 instance.

### Step 2.1: Update System

```bash
sudo apt update
sudo apt upgrade -y
```

### Step 2.2: Install Docker

```bash
# Install dependencies
sudo apt install -y ca-certificates curl gnupg lsb-release

# Add Docker GPG key
sudo mkdir -p /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg

# Add Docker repository
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add user to docker group
sudo usermod -aG docker ubuntu

# Logout and login for group changes
exit
```

Reconnect via SSH, then verify:

```bash
docker --version
docker compose version
```

### Step 2.3: Install Nginx

```bash
sudo apt install -y nginx

# Verify
nginx -v
sudo systemctl status nginx
```

### Step 2.4: Install Git

```bash
sudo apt install -y git
git --version
```

---

## Part 3: Clone and Configure Application

### Step 3.1: Clone Repository

```bash
cd ~
git clone https://github.com/YOUR_USERNAME/StarterKit.git
cd StarterKit
```

Replace YOUR_USERNAME with your GitHub username.

### Step 3.2: Create Production Environment File

```bash
cd ~/StarterKit
cp deployment/.env.production.example .env.production
```

Edit the file:

```bash
nano .env.production
```

Update these critical values:

```env
# Database
POSTGRES_PASSWORD=your_secure_password_here_32chars

# Redis
REDIS_PASSWORD=your_redis_password_here_24chars

# JWT
JWT_SECRET_KEY=your_jwt_secret_64_random_chars_here
JWT_ISSUER=https://YOUR_ELASTIC_IP
JWT_AUDIENCE=https://YOUR_ELASTIC_IP

# API URLs
API_BASE_URL=http://YOUR_ELASTIC_IP
CORS_ORIGINS=http://YOUR_ELASTIC_IP,https://YOUR_ELASTIC_IP

# Email (if using)
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USER=your_email@gmail.com
SMTP_PASSWORD=your_app_password
FROM_EMAIL=noreply@yourdomain.com
```

Generate secure passwords:

```bash
# PostgreSQL password (32 chars)
openssl rand -base64 32

# Redis password (24 chars)
openssl rand -base64 24

# JWT secret (64 chars)
openssl rand -base64 64
```

Save and exit (Ctrl+X, Y, Enter).

### Step 3.3: Configure Nginx

Copy production nginx configurations:

```bash
# Copy main nginx configuration (global settings)
sudo cp ~/StarterKit/deployment/nginx/nginx.conf /etc/nginx/nginx.conf

# Copy site configuration
sudo cp ~/StarterKit/deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit
```

Edit site configuration to set your server name (optional - default accepts any):

```bash
sudo nano /etc/nginx/sites-available/starterkit
```

Find `server_name _;` and optionally replace with your IP or domain:

```nginx
server_name YOUR_DOMAIN_OR_IP;
```

Enable the site:

```bash
# Disable default site
sudo rm /etc/nginx/sites-enabled/default

# Enable StarterKit site
sudo ln -s /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/

# Test configuration
sudo nginx -t

# Expected output: "configuration file /etc/nginx/nginx.conf test is successful"

# Reload Nginx
sudo systemctl reload nginx
```

---

## Part 4: Deploy Application

### Step 4.1: Build and Start Services

```bash
cd ~/StarterKit

# Make deploy script executable
chmod +x deployment/deploy.sh

# Run deployment
./deployment/deploy.sh
```

This will:
- Pull/build Docker images
- Start PostgreSQL, Redis
- Run database migrations
- Start 2 API instances
- Start monitoring stack (Seq, Prometheus, Grafana)
- Run health checks

Wait 2-3 minutes for startup.

### Step 4.2: Verify Deployment

Check service status:

```bash
docker ps
```

You should see:
- starterkit-api-1
- starterkit-api-2
- starterkit-postgres-1
- starterkit-redis-1
- starterkit-seq-1
- starterkit-prometheus-1
- starterkit-grafana-1

Check logs:

```bash
# API logs
docker logs starterkit-api-1
docker logs starterkit-api-2

# Database logs
docker logs starterkit-postgres-1
```

### Step 4.3: Test Application

From your local machine:

```bash
# Health check
curl http://YOUR_ELASTIC_IP/health

# API test
curl http://YOUR_ELASTIC_IP/api/v1/health
```

Expected response: HTTP 200 OK

Test in browser:
- API: http://YOUR_ELASTIC_IP/swagger
- Seq Logs: http://YOUR_ELASTIC_IP:5341
- Grafana: http://YOUR_ELASTIC_IP:3000 (admin/admin)

---

## Part 5: Setup Domain and SSL (Optional)

Skip this section if using IP only.

### Step 5.1: Point Domain to IP

In your domain registrar (GoDaddy, Namecheap, etc):

1. Add A record:
   - Host: @ (or subdomain like "api")
   - Points to: YOUR_ELASTIC_IP
   - TTL: 600

2. Wait 5-10 minutes for DNS propagation

3. Verify:
```bash
nslookup yourdomain.com
```

### Step 5.2: Install Certbot

```bash
sudo apt install -y certbot python3-certbot-nginx
```

### Step 5.3: Obtain SSL Certificate

```bash
sudo certbot --nginx -d yourdomain.com
```

Follow prompts:
- Enter email address
- Agree to terms
- Choose redirect HTTP to HTTPS: Yes

Certbot will:
- Obtain certificate from Let's Encrypt
- Modify Nginx configuration
- Setup auto-renewal

### Step 5.4: Update Environment Variables

```bash
nano .env.production
```

Update URLs to use https:

```env
JWT_ISSUER=https://yourdomain.com
JWT_AUDIENCE=https://yourdomain.com
API_BASE_URL=https://yourdomain.com
CORS_ORIGINS=https://yourdomain.com
```

Restart services:

```bash
cd ~/StarterKit
docker compose -f docker-compose.yml -f deployment/docker-compose.production.yml restart api1 api2
```

### Step 5.5: Test SSL

```bash
curl https://yourdomain.com/health
```

Test in browser: https://yourdomain.com/swagger

---

## Part 6: Configure GitHub Secrets

Required for CI/CD automation.

### Step 6.1: Prepare SSH Private Key

On your local machine, open the .pem file:

```bash
cat starterkit-production.pem
```

Copy the ENTIRE content including:
```
-----BEGIN RSA PRIVATE KEY-----
...all lines...
-----END RSA PRIVATE KEY-----
```

### Step 6.2: Add Secrets to GitHub

1. Go to your GitHub repository
2. Settings → Secrets and variables → Actions
3. Click "New repository secret"

Add these 4 secrets:

**Secret 1: EC2_HOST**
- Name: EC2_HOST
- Value: YOUR_ELASTIC_IP (or yourdomain.com)

**Secret 2: EC2_USER**
- Name: EC2_USER
- Value: ubuntu

**Secret 3: EC2_SSH_PRIVATE_KEY**
- Name: EC2_SSH_PRIVATE_KEY
- Value: Paste entire .pem file content (including BEGIN/END lines)

**Secret 4: PRODUCTION_URL**
- Name: PRODUCTION_URL
- Value: http://YOUR_ELASTIC_IP (or https://yourdomain.com)

### Step 6.3: Verify Secrets

All 4 secrets should show in the list:
- EC2_HOST
- EC2_USER
- EC2_SSH_PRIVATE_KEY
- PRODUCTION_URL

---

## Part 7: Setup CI/CD Workflows

### Step 7.1: Update Docker Image Names

Edit docker-compose.yml:

```bash
nano docker-compose.yml
```

Find the api service, change image to:

```yaml
api:
  image: ghcr.io/YOUR_GITHUB_USERNAME/starterkit-api:latest
```

Edit deployment/docker-compose.production.yml:

```bash
nano deployment/docker-compose.production.yml
```

Update api2 service image:

```yaml
api2:
  image: ghcr.io/YOUR_GITHUB_USERNAME/starterkit-api:latest
```

Replace YOUR_GITHUB_USERNAME with your GitHub username (lowercase).

### Step 7.2: Enable GitHub Container Registry

1. GitHub → Settings → Developer settings
2. Personal access tokens → Tokens (classic)
3. Generate new token
4. Select scopes:
   - write:packages
   - read:packages
   - delete:packages
5. Generate token
6. Copy token

Add as repository secret:
- Name: GHCR_TOKEN
- Value: Paste token

### Step 7.3: Commit and Push Changes

```bash
git add docker-compose.yml deployment/docker-compose.production.yml
git commit -m "Configure production deployment"
git push origin main
```

### Step 7.4: Verify Workflow Execution

1. Go to repository → Actions tab
2. You should see "CI/CD Production" workflow running
3. Click on the workflow run to see progress

Workflow steps:
1. Build & Test (3-5 minutes)
2. Build & Push Docker Images (2-3 minutes)
3. Security Scan with Trivy (1-2 minutes)
4. Deploy to Production (2-3 minutes)
5. Health Checks (30 seconds)

Total time: 10-15 minutes

### Step 7.5: Verify Deployment

After workflow completes successfully:

```bash
curl http://YOUR_ELASTIC_IP/health
```

Check EC2 to see new deployment:

```bash
ssh -i starterkit-production.pem ubuntu@YOUR_ELASTIC_IP
cd ~/StarterKit
docker ps
docker logs starterkit-api-1 --tail 50
```

---

## Part 8: CI/CD Usage

### Automatic Deployment

Every push to main branch triggers automatic deployment:

```bash
# Make a change
git add .
git commit -m "Update feature"
git push origin main
```

GitHub Actions will automatically:
1. Build and test
2. Build Docker images
3. Security scan
4. Backup database
5. Deploy with zero downtime
6. Run health checks
7. Rollback if health checks fail

### Manual Deployment

To deploy specific branch or tag:

1. Go to Actions → Manual Deploy Production
2. Click "Run workflow"
3. Select branch/tag
4. Choose options:
   - Skip database backup: No
   - Skip health checks: No
5. Click "Run workflow"

### Rollback

If deployment has issues:

1. Go to Actions → Rollback Production
2. Click "Run workflow"
3. Enter backup filename (shown in deployment logs)
4. Confirm: yes
5. Click "Run workflow"

This will:
- Stop current services
- Restore database from backup
- Restart previous version
- Verify health

### Health Monitoring

Automated health checks run every hour:

1. Actions → Health Monitoring Production
2. View latest run for system status

Checks:
- API health endpoints
- Database connectivity
- Redis connectivity
- Disk space
- Memory usage
- Container status

---

## Part 9: Monitoring and Maintenance

### Access Monitoring Dashboards

**Seq (Logs)**
- URL: http://YOUR_ELASTIC_IP:5341
- No login required (secure in production with IP whitelist)

**Grafana (Metrics)**
- URL: http://YOUR_ELASTIC_IP:3000
- Default: admin/admin (change on first login)

**Prometheus (Metrics Backend)**
- URL: http://YOUR_ELASTIC_IP:9090
- No authentication

### View Logs

```bash
# API logs (real-time)
docker logs -f starterkit-api-1

# API logs (last 100 lines)
docker logs starterkit-api-1 --tail 100

# All services
docker compose logs

# Specific service
docker compose logs postgres
docker compose logs redis
```

### Database Backup

Manual backup:

```bash
cd ~/StarterKit
./deployment/scripts/backup-db.sh
```

Backups stored in: ~/StarterKit/backups/

Automatic backups:
- Before every deployment
- Retention: 7 days

### Database Restore

```bash
cd ~/StarterKit
./deployment/scripts/restore-db.sh backup_filename.sql
```

List available backups:

```bash
ls -lh ~/StarterKit/backups/
```

### Restart Services

```bash
cd ~/StarterKit

# Restart all
docker compose restart

# Restart specific service
docker compose restart api1
docker compose restart api2
docker compose restart postgres
```

### Update Application

Automatic: Just push to main branch

Manual:

```bash
cd ~/StarterKit
git pull origin main
./deployment/deploy.sh
```

### Check System Resources

```bash
# Disk space
df -h

# Memory usage
free -h

# Docker resource usage
docker stats

# Container status
docker ps -a
```

### Clean Up Old Images

```bash
# Remove unused images
docker image prune -a

# Remove stopped containers
docker container prune

# Remove unused volumes
docker volume prune
```

---

## Part 10: Troubleshooting

### Issue: Cannot connect to EC2

**Check:**
```bash
# Verify security group allows your IP
# Check instance is running
# Verify Elastic IP is associated
```

**Fix:**
- Update security group with your current IP
- Start instance if stopped
- Re-associate Elastic IP

### Issue: Nginx 502 Bad Gateway

**Check:**
```bash
sudo nginx -t
docker ps
docker logs starterkit-api-1
```

**Common causes:**
- API containers not running
- Wrong ports in Nginx config
- API startup not complete (wait 2 minutes)

**Fix:**
```bash
docker compose restart api1 api2
sudo systemctl restart nginx
```

### Issue: Database connection error

**Check:**
```bash
docker logs starterkit-postgres-1
docker exec -it starterkit-postgres-1 psql -U postgres -d starterkit
```

**Fix:**
```bash
# Restart database
docker compose restart postgres

# Check password in .env.production
nano .env.production

# Recreate if needed
docker compose down postgres
docker compose up -d postgres
```

### Issue: CI/CD workflow fails at SSH

**Check:**
- EC2_SSH_PRIVATE_KEY includes BEGIN/END lines
- EC2_HOST is correct (IP or domain)
- Security group allows SSH from anywhere (0.0.0.0/0) or GitHub IP ranges

**Fix:**
```bash
# Test SSH manually
ssh -i key.pem ubuntu@YOUR_IP

# Update security group if needed
# Re-add SSH private key secret with full content
```

### Issue: Docker out of disk space

**Check:**
```bash
df -h
docker system df
```

**Fix:**
```bash
# Clean up
docker system prune -a --volumes

# If still full, increase EBS volume in AWS
```

### Issue: High memory usage

**Check:**
```bash
free -h
docker stats
```

**Fix:**
```bash
# Stop observability stack if not needed
cd ~/StarterKit
docker compose stop seq prometheus grafana

# Or upgrade to t3.large (8GB RAM)
```

### Issue: SSL certificate not renewing

**Check:**
```bash
sudo certbot certificates
sudo certbot renew --dry-run
```

**Fix:**
```bash
# Force renewal
sudo certbot renew --force-renewal
sudo systemctl reload nginx
```

### View Detailed Logs

```bash
# Deployment script log
cat ~/StarterKit/deployment.log

# Nginx error log
sudo tail -f /var/log/nginx/error.log

# System log
sudo journalctl -u nginx -f
```

---

## Part 11: Security Hardening

### Enable Firewall

```bash
# Install UFW
sudo apt install -y ufw

# Allow SSH
sudo ufw allow 22/tcp

# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Enable firewall
sudo ufw enable

# Check status
sudo ufw status
```

### Secure Monitoring Endpoints

Update security group:
- Restrict ports 3000, 5341, 9090 to your IP only
- Or use SSH tunneling

SSH tunnel example:

```bash
# From local machine
ssh -i key.pem -L 3000:localhost:3000 ubuntu@YOUR_IP
ssh -i key.pem -L 5341:localhost:5341 ubuntu@YOUR_IP
```

Then access:
- http://localhost:3000 (Grafana)
- http://localhost:5341 (Seq)

### Setup Automatic Updates

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
```

### Enable Fail2Ban

```bash
sudo apt install -y fail2ban
sudo systemctl enable fail2ban
sudo systemctl start fail2ban
```

### Regular Security Updates

```bash
# Run weekly
sudo apt update
sudo apt upgrade -y
```

---

## Part 12: Performance Optimization

### Enable Docker BuildKit

```bash
echo 'export DOCKER_BUILDKIT=1' >> ~/.bashrc
source ~/.bashrc
```

### Optimize Postgres

Edit postgresql.conf in Docker:

```bash
docker exec -it starterkit-postgres-1 bash
nano /var/lib/postgresql/data/postgresql.conf
```

Add:

```
shared_buffers = 256MB
effective_cache_size = 1GB
maintenance_work_mem = 64MB
checkpoint_completion_target = 0.9
wal_buffers = 16MB
```

Restart:

```bash
docker compose restart postgres
```

### Enable Redis Persistence

Edit .env.production:

```env
REDIS_MAXMEMORY=256mb
REDIS_POLICY=allkeys-lru
```

Restart:

```bash
docker compose restart redis
```

### Nginx Caching

Already configured in app-production.conf:
- Static files cached for 1 year
- API responses cached for 5 minutes
- Browser caching enabled

### Monitor Performance

Use Grafana dashboards:
- CPU usage per container
- Memory usage trends
- Network I/O
- Response times

---

## Part 13: Scaling Considerations

### Current Setup (t3.medium)

- 2 API instances
- 1 Database
- 1 Redis
- Monitoring stack

Handles: 100-500 concurrent users

### Vertical Scaling (More Resources)

Upgrade to t3.large:
- 2 vCPU → 4 vCPU
- 4 GB RAM → 8 GB RAM
- Cost: ~$60/month

Update resource limits in docker-compose.production.yml

### Horizontal Scaling (More Instances)

For 1000+ concurrent users:

1. Setup Application Load Balancer
2. Launch multiple EC2 instances
3. Use RDS for PostgreSQL (managed database)
4. Use ElastiCache for Redis (managed cache)
5. Update Nginx to proxy to ALB

### Database Optimization

For heavy database load:
- Move to RDS PostgreSQL (managed, auto-scaling)
- Enable read replicas
- Use connection pooling (already configured via EF Core)

---

## Quick Reference

### Essential Commands

```bash
# Check status
docker ps
docker compose logs

# Restart application
cd ~/StarterKit && docker compose restart api1 api2

# Update application
cd ~/StarterKit && git pull && ./deployment/deploy.sh

# Backup database
cd ~/StarterKit && ./deployment/scripts/backup-db.sh

# View logs
docker logs starterkit-api-1 --tail 100

# Health check
curl http://YOUR_IP/health

# Check resources
docker stats
df -h
free -h
```

### Important Files

```
~/StarterKit/.env.production          - Environment variables
~/StarterKit/deployment/deploy.sh     - Main deployment script
~/StarterKit/backups/                 - Database backups
/etc/nginx/sites-available/starterkit - Nginx configuration
/var/log/nginx/                    - Nginx logs
```

### Important URLs

```
Application:    http://YOUR_IP
API Swagger:    http://YOUR_IP/swagger
Health Check:   http://YOUR_IP/health
Seq Logs:       http://YOUR_IP:5341
Grafana:        http://YOUR_IP:3000
Prometheus:     http://YOUR_IP:9090
```

### GitHub Actions Workflows

```
.github/workflows/ci-cd-production.yml     - Automatic deployment
.github/workflows/manual-deploy.yml        - Manual deployment
.github/workflows/rollback.yml             - Rollback deployment
.github/workflows/health-monitoring.yml    - Health checks
```

### Support Contacts

- AWS Support: aws.amazon.com/support
- GitHub Support: support.github.com
- Let's Encrypt Status: letsencrypt.status.io

---

## Success Checklist

Mark each item when complete:

**Part 1: EC2 Instance**
- [ ] EC2 instance launched (t3.medium)
- [ ] Elastic IP allocated and associated
- [ ] Security group configured
- [ ] SSH connection working

**Part 2: Software Installation**
- [ ] System updated
- [ ] Docker installed
- [ ] Nginx installed
- [ ] Git installed

**Part 3: Application Setup**
- [ ] Repository cloned
- [ ] .env.production configured
- [ ] Nginx configuration updated
- [ ] Nginx reloaded

**Part 4: Deployment**
- [ ] Application deployed
- [ ] All containers running
- [ ] Health checks passing
- [ ] Swagger accessible

**Part 5: Domain & SSL (Optional)**
- [ ] Domain pointed to IP
- [ ] SSL certificate obtained
- [ ] HTTPS working
- [ ] Auto-renewal configured

**Part 6: GitHub Secrets**
- [ ] EC2_HOST added
- [ ] EC2_USER added
- [ ] EC2_SSH_PRIVATE_KEY added
- [ ] PRODUCTION_URL added
- [ ] GHCR_TOKEN added

**Part 7: CI/CD Setup**
- [ ] Docker image names updated
- [ ] Changes committed and pushed
- [ ] First workflow executed successfully
- [ ] Automatic deployment verified

**Part 8: Testing**
- [ ] Manual deployment tested
- [ ] Rollback tested
- [ ] Health monitoring verified

**Part 9: Monitoring**
- [ ] Seq accessible
- [ ] Grafana configured
- [ ] Logs verified
- [ ] Backup tested

**Part 10: Security**
- [ ] Firewall enabled
- [ ] Monitoring endpoints secured
- [ ] Automatic updates enabled

---

## Cost Breakdown

Monthly costs for t3.medium setup:

```
EC2 t3.medium (730 hours):        $30.37
EBS 30GB gp3:                     $2.40
Elastic IP (if unused):           $0.00
Data Transfer (100GB):            $9.00
Total:                            ~$42/month
```

Optimizations:
- Reserved Instance: Save 40% ($18/month savings)
- Spot Instance: Save 70% (risky for production)
- Right-size after testing: Switch to t3.small if under-utilized

---

## Completion

You now have:

✅ Production-ready EC2 infrastructure
✅ Containerized application with Docker
✅ Load balanced API with Nginx
✅ Full CI/CD pipeline with GitHub Actions
✅ Zero-downtime deployments
✅ Automated health monitoring
✅ Database backups before each deploy
✅ One-click rollback capability
✅ Comprehensive monitoring (Seq, Grafana, Prometheus)
✅ SSL/TLS support (if domain configured)

Your application is production-ready with enterprise-grade deployment automation.

Push code → Automatic build → Test → Security scan → Deploy → Health check → Done

Total deployment time: 10-15 minutes automated

---

**Document Version**: 1.0  
**Last Updated**: 2026-01-22  
**Application**: StarterKit Modular Monolith  
**Infrastructure**: AWS EC2 + Docker + Nginx + GitHub Actions
