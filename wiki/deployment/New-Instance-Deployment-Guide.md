# StarterKit — Deploy to New AWS EC2 Instance (From Zero to Production)

> **Audience**: Senior engineer deploying StarterKit to a fresh AWS account
> **Total time**: ~2.5–3 hours (excluding DNS propagation)
> **Security**: Dedicated `deploy` user (never root), fail2ban, SSH hardening, swap
> **Prerequisites**: AWS account, domain name, GitHub repo access

---

## Phase 1: AWS EC2 Instance Setup (~15 min)

### Step 1.1 — Launch EC2 Instance

1. AWS Console → EC2 → **Launch Instance**
2. Configuration:
   - **Name**: `starterkit-production`
   - **AMI**: Ubuntu 22.04 LTS (HVM, SSD)
   - **Instance type**: `t3.medium` (2 vCPU, 4GB RAM) — minimum recommended
   - **Key pair**: Create new → download `.pem` file → save securely
   - **Storage**: 30GB gp3 minimum, **50GB recommended**
   - **Security Group** — create new with these inbound rules:

| Port | Protocol | Source | Purpose |
|------|----------|--------|---------|
| 22 | TCP | Your IP only | SSH |
| 80 | TCP | 0.0.0.0/0 | HTTP (redirects to HTTPS) |
| 443 | TCP | 0.0.0.0/0 | HTTPS |

3. Launch the instance
4. **Elastic IP** — allocate and associate (so the IP survives reboots):
   - EC2 → Elastic IPs → Allocate → Associate with your instance
   - Note this IP: `<YOUR_EC2_IP>`

### Step 1.2 — DNS Configuration

1. Go to your domain registrar (Namecheap, Route53, Cloudflare, etc.)
2. Add DNS record:
   - **A record** `api` → `<YOUR_EC2_IP>` (TTL: 300)
3. Verify propagation:
   ```bash
   dig +short api.yourdomain.com
   ```

---

## Phase 2: Create Deploy User + Harden SSH (~8 min)

> **Why?** AWS EC2 defaults to the `ubuntu` user with sudo access. Best practice: create a dedicated `deploy` user, harden SSH, then use that user for everything. Never run production workloads as root.

### Step 2.1 — SSH as ubuntu & Create Deploy User

```bash
ssh -i ~/path/to/your-key.pem ubuntu@<YOUR_EC2_IP>

# Create deploy user with sudo
sudo adduser --disabled-password --gecos "" deploy
sudo usermod -aG sudo deploy

# Allow sudo without password (needed for GitHub Actions runner)
echo "deploy ALL=(ALL) NOPASSWD:ALL" | sudo tee /etc/sudoers.d/deploy

# Copy SSH key to deploy user
sudo mkdir -p /home/deploy/.ssh
sudo cp ~/.ssh/authorized_keys /home/deploy/.ssh/
sudo chown -R deploy:deploy /home/deploy/.ssh
sudo chmod 700 /home/deploy/.ssh
sudo chmod 600 /home/deploy/.ssh/authorized_keys
```

### Step 2.2 — Test Deploy User Login (New Terminal)

```bash
# Open a NEW terminal — keep the ubuntu session open
ssh -i ~/path/to/your-key.pem deploy@<YOUR_EC2_IP>
sudo whoami  # should print: root
```

### Step 2.3 — Harden SSH (Back in Ubuntu Session)

```bash
sudo sed -i 's/#\?PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config
sudo sed -i 's/#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo sed -i 's/#\?PubkeyAuthentication.*/PubkeyAuthentication yes/' /etc/ssh/sshd_config
sudo sed -i 's/#\?MaxAuthTries.*/MaxAuthTries 3/' /etc/ssh/sshd_config

sudo systemctl restart ssh
```

> **WARNING**: Keep your ubuntu session open until you verify the `deploy` user works. If locked out, use AWS EC2 Serial Console or instance recovery.

### Step 2.4 — Verify & Switch to Deploy User

```bash
# From your local machine, verify deploy user works
ssh -i ~/path/to/your-key.pem deploy@<YOUR_EC2_IP>

# From now on, ALL remaining steps use the deploy user
```

---

## Phase 3: Server Base Setup (~15 min)

> All commands below as the `deploy` user.

### Step 3.1 — System Update

```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Africa/Cairo
sudo hostnamectl set-hostname starterkit-production
```

### Step 3.2 — Install Docker & Docker Compose

```bash
# Prerequisites
sudo apt install -y apt-transport-https ca-certificates curl software-properties-common gnupg lsb-release

# Docker repository
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Allow deploy user to run docker without sudo
sudo usermod -aG docker deploy
newgrp docker

# Verify
docker --version
docker compose version
```

### Step 3.3 — Install Nginx & Certbot

```bash
sudo apt install -y nginx certbot python3-certbot-nginx
nginx -v
certbot --version
```

### Step 3.4 — Install Additional Tools

```bash
sudo apt install -y git jq htop unzip openssl fail2ban
```

### Step 3.5 — Configure fail2ban

```bash
sudo systemctl enable fail2ban
sudo systemctl start fail2ban

# Verify SSH jail is active
sudo fail2ban-client status sshd
```

---

## Phase 4: UFW Firewall (~5 min)

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing

# CRITICAL: Allow SSH BEFORE enabling UFW
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

sudo ufw enable
sudo ufw status verbose
```

**Expected output:**
```
To                         Action      From
--                         ------      ----
22/tcp                     ALLOW       Anywhere
80/tcp                     ALLOW       Anywhere
443/tcp                    ALLOW       Anywhere
```

> **WARNING**: Always allow SSH (port 22) before enabling UFW or you will lock yourself out.

---

## Phase 5: Project Setup (~10 min)

### Step 5.1 — Clone Repository

```bash
sudo mkdir -p /opt/starterkit
sudo chown deploy:deploy /opt/starterkit
cd /opt/starterkit

git clone https://github.com/your-org/StarterKit.git .
```

### Step 5.2 — Generate Production Secrets

```bash
cd /opt/starterkit
mkdir -p secrets

chmod +x deployment/secrets-setup.sh
./deployment/secrets-setup.sh

# Verify (expect: postgres_password.txt, redis_password.txt, jwt_secret.txt)
ls -la secrets/
```

### Step 5.3 — Generate Seq Admin Password

```bash
chmod +x deployment/secure-seq.sh
./deployment/secure-seq.sh

# Copy the SEQ_ADMIN_PASSWORD_HASH output — you'll need it next
# Password is saved to secrets/seq_admin_password.txt
```

### Step 5.4 — Create Production Environment File

```bash
cp deployment/.env.production.example .env.prod
nano .env.prod
```

**Critical values to update:**

```env
# === MUST CHANGE ===
COMPOSE_PROJECT_NAME=starterkit
ASPNETCORE_ENVIRONMENT=Production
TZ=Africa/Cairo

# Docker image
DOCKER_IMAGE=ghcr.io/your-org/starterkit/starterkit-api
DOCKER_IMAGE_TAG=latest

# Database (passwords read from secrets/ files — don't set here)
POSTGRES_DB=starterkit
POSTGRES_USER=starterkit_user

# Redis
REDIS_MAXMEMORY=512mb

# JWT
JWT_ISSUER=StarterKitAPI
JWT_AUDIENCE=StarterKitClient
JWT_EXPIRATION_MINUTES=15
JWT_REFRESH_EXPIRATION_DAYS=7

# Seq
SEQ_ACCEPT_EULA=Y
SEQ_ADMIN_PASSWORD_HASH=<PASTE_HASH_FROM_STEP_5.3>

# API
API_PORT=5000

# S3 Backup (optional — see Phase 11)
S3_BACKUP_BUCKET=<YOUR_S3_BUCKET_NAME>
AWS_ACCESS_KEY_ID=<YOUR_AWS_KEY>
AWS_SECRET_ACCESS_KEY=<YOUR_AWS_SECRET>
AWS_DEFAULT_REGION=us-east-1

# CORS — your frontend domain(s)
CORS_ALLOWED_ORIGINS=https://yourdomain.com,https://www.yourdomain.com
```

### Step 5.5 — Create Symlink

```bash
# deploy.sh reads .env.prod, Docker Compose reads .env
ln -sf .env.prod .env
```

---

## Phase 6: Nginx Configuration (~10 min)

### Step 6.1 — Deploy Main Nginx Config

```bash
sudo cp /etc/nginx/nginx.conf /etc/nginx/nginx.conf.bak
sudo cp /opt/starterkit/deployment/nginx/nginx.conf /etc/nginx/nginx.conf
```

### Step 6.2 — Deploy Site Configuration (HTTP-only first)

```bash
sudo rm -f /etc/nginx/sites-enabled/default
sudo cp /opt/starterkit/deployment/nginx/app-production.conf /etc/nginx/sites-available/starterkit

# Update domain name
sudo nano /etc/nginx/sites-available/starterkit
# Change: server_name api.example.com;
# To:     server_name yourdomain.com www.yourdomain.com;

# Enable site
sudo ln -sf /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/starterkit

# Create certbot webroot
sudo mkdir -p /var/www/certbot

# Test and reload
sudo nginx -t
sudo systemctl reload nginx
sudo systemctl enable nginx
```

### Step 6.3 — Verify Upstream Ports

Confirm the upstream block in `/etc/nginx/nginx.conf` has:
```nginx
upstream starterkit_backend {
    least_conn;
    server localhost:5000 max_fails=3 fail_timeout=30s;
    server localhost:5001 max_fails=3 fail_timeout=30s;
    keepalive 32;
}
```

---

## Phase 7: Docker — First Launch (~10 min)

### Step 7.1 — Login to GHCR

```bash
# Need a GitHub PAT with read:packages scope
echo "<YOUR_GITHUB_PAT>" | docker login ghcr.io -u <YOUR_GITHUB_USERNAME> --password-stdin
```

### Step 7.2 — Pull and Start

```bash
cd /opt/starterkit
docker compose pull
docker compose -f docker-compose.yml up -d

# Watch logs until healthy
docker compose logs -f api
# Ctrl+C to exit
```

### Step 7.3 — Verify

```bash
docker compose ps
# Expected: postgres (healthy), redis (healthy), seq (started), api (healthy)

curl -s http://localhost:5000/health
curl -s http://localhost:5000/alive
```

### Step 7.4 — Verify from Outside

```bash
# From your LOCAL machine
curl -s http://yourdomain.com/health
```

---

## Phase 8: SSL Certificate (~5 min)

### Step 8.1 — Obtain Certificate

```bash
sudo certbot --nginx -d yourdomain.com -d www.yourdomain.com

# Enter email, agree to ToS, choose redirect (option 2)
```

### Step 8.2 — Deploy Hardened SSL Config

```bash
sudo cp /opt/starterkit/deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit

# Update for your domain
sudo nano /etc/nginx/sites-available/starterkit
# Replace ALL "api.example.com" → "yourdomain.com"
# Update SSL paths:
#   ssl_certificate /etc/letsencrypt/live/yourdomain.com/fullchain.pem;
#   ssl_certificate_key /etc/letsencrypt/live/yourdomain.com/privkey.pem;
#   ssl_trusted_certificate /etc/letsencrypt/live/yourdomain.com/chain.pem;
# Update IP whitelist (YOUR_ADMIN_IP → YOUR public IP)

sudo nginx -t
sudo systemctl reload nginx
```

### Step 8.3 — Verify SSL

```bash
curl -s https://yourdomain.com/health
sudo certbot renew --dry-run
sudo certbot certificates
```

**SSL grade check**: Visit `https://www.ssllabs.com/ssltest/analyze.html?d=yourdomain.com` — expect **A+**

---

## Phase 9: GitHub Self-Hosted Runner (~15 min)

### Step 9.1 — Get Runner Token

Go to: `https://github.com/your-org/StarterKit/settings/actions/runners/new` → Select **Linux x64** → Copy token

### Step 9.2 — Install Runner

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner

# Download (check GitHub for latest version)
curl -o actions-runner-linux-x64-2.322.0.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.322.0/actions-runner-linux-x64-2.322.0.tar.gz
tar xzf ./actions-runner-linux-x64-2.322.0.tar.gz

# Configure
./config.sh --url https://github.com/your-org/StarterKit \
  --token <REGISTRATION_TOKEN> \
  --name starterkit-deploy-runner \
  --labels self-hosted,deploy-only \
  --work _work \
  --runasservice

# Install and start as service
sudo ./svc.sh install
sudo ./svc.sh start
sudo ./svc.sh status
```

### Step 9.3 — Verify Runner

Go to: `https://github.com/your-org/StarterKit/settings/actions/runners` — runner should show **Idle** (green).

---

## Phase 10: GitHub Repository Secrets (~5 min)

Go to: `https://github.com/your-org/StarterKit/settings/secrets/actions`

| Secret Name | Value | Source |
|-------------|-------|--------|
| `EC2_HOST` | `<YOUR_EC2_IP>` | Your Elastic IP |
| `EC2_USER` | `deploy` | Deploy user (created in Phase 2) |
| `SSH_PRIVATE_KEY` | `.pem` file contents | `cat ~/your-key.pem` |
| `POSTGRES_PASSWORD` | DB password | `cat /opt/starterkit/secrets/postgres_password.txt` |
| `REDIS_PASSWORD` | Redis password | `cat /opt/starterkit/secrets/redis_password.txt` |
| `JWT_SECRET` | JWT key | `cat /opt/starterkit/secrets/jwt_secret.txt` |
| `SEQ_ADMIN_PASSWORD_HASH` | Seq hash | Output from Step 5.3 |
| `TELEGRAM_BOT_TOKEN` | *(optional)* | @BotFather |
| `TELEGRAM_CHAT_ID` | *(optional)* | Your chat ID |

> `GITHUB_TOKEN` is automatic — no need to add it.

---

## Phase 11: S3 Backup Configuration (Optional, Recommended) (~10 min)

### Step 11.1 — Create S3 Bucket

- AWS Console → S3 → Create Bucket
  - **Name**: `starterkit-backups-<your-id>`
  - **Region**: Same as EC2
  - **Block all public access**: ON
  - **Versioning**: Enabled
  - **Encryption**: SSE-S3

### Step 11.2 — Create IAM User

- AWS Console → IAM → Users → Create User: `starterkit-backup-user`
- Attach inline policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:GetObject", "s3:ListBucket", "s3:DeleteObject"],
      "Resource": [
        "arn:aws:s3:::starterkit-backups-<your-id>",
        "arn:aws:s3:::starterkit-backups-<your-id>/*"
      ]
    }
  ]
}
```

- Create access key → update `.env.prod` with `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`

### Step 11.3 — Verify Backup

```bash
cd /opt/starterkit
chmod +x deployment/scripts/backup-db.sh
./deployment/scripts/backup-db.sh
ls -la backups/
```

---

## Phase 12: Deploy Script Permissions (~2 min)

```bash
cd /opt/starterkit
chmod +x deployment/deploy.sh
chmod +x deployment/rollback.sh
chmod +x deployment/pre-deploy-backup.sh
chmod +x deployment/secrets-setup.sh
chmod +x deployment/secure-seq.sh
chmod +x deployment/scripts/*.sh
```

---

## Phase 13: First CI/CD Deployment Test (~10 min)

### Step 13.1 — Trigger

Either push to `master` or:
1. Go to `https://github.com/your-org/StarterKit/actions/workflows/hybrid-ci-cd.yml`
2. **Run workflow** → `master` → **Run workflow**

### Step 13.2 — Monitor

Watch in GitHub Actions:
1. **build-and-test** → .NET build + tests
2. **build-and-push** → Docker image → GHCR
3. **deploy** → self-hosted runner pulls and deploys

### Step 13.3 — Verify

```bash
docker compose ps
curl -s https://yourdomain.com/health
curl -s https://yourdomain.com/alive
docker compose logs --tail=50 api
```

---

## Phase 14: Post-Deployment Security Hardening (~10 min)

> SSH hardening was already done in Phase 2 (PermitRootLogin, PasswordAuthentication, MaxAuthTries).

### Step 14.1 — Setup Swap File (Recommended for 4GB servers)

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# Verify
free -h
```

### Step 14.2 — Update Nginx IP Whitelist

```bash
sudo nano /etc/nginx/sites-available/starterkit
# For /swagger, /scalar, /seq/, /grafana/, /prometheus/, /jaeger/:
# Replace YOUR_ADMIN_IP → YOUR public IP

sudo nginx -t && sudo systemctl reload nginx
```

### Step 14.3 — Automatic Security Updates

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
# Select "Yes"
```

---

## Phase 15: Verification Checklist

```bash
# 1. Docker services
docker compose ps

# 2. Health endpoints
curl -s http://localhost:5000/health | jq .
curl -s http://localhost:5000/alive

# 3. HTTPS working
curl -s https://yourdomain.com/health

# 4. HTTP → HTTPS redirect
curl -sI http://yourdomain.com | head -5

# 5. SSL certificate valid
echo | openssl s_client -servername yourdomain.com -connect yourdomain.com:443 2>/dev/null | openssl x509 -noout -dates

# 6. Certbot auto-renewal
sudo certbot renew --dry-run

# 7. Firewall active
sudo ufw status

# 8. Runner active
sudo ~/actions-runner/svc.sh status

# 9. Secrets permissions
ls -la /opt/starterkit/secrets/

# 10. Disk space
df -h /

# 11. Memory
free -h
```

---

## Quick Reference — Paths

| Item | Path |
|------|------|
| Project root | `/opt/starterkit/` |
| Docker Compose | `/opt/starterkit/docker-compose.yml` |
| Environment | `/opt/starterkit/.env.prod` |
| Secrets | `/opt/starterkit/secrets/*.txt` |
| Backups | `/opt/starterkit/backups/` |
| Nginx main | `/etc/nginx/nginx.conf` |
| Nginx site | `/etc/nginx/sites-available/starterkit` |
| SSL certs | `/etc/letsencrypt/live/yourdomain.com/` |
| Nginx logs | `/var/log/nginx/` |
| GitHub runner | `~/actions-runner/` |
| Deploy script | `/opt/starterkit/deployment/deploy.sh` |
| Rollback | `/opt/starterkit/deployment/rollback.sh` |

## Quick Reference — Common Operations

```bash
docker compose logs -f api              # API logs
cd /opt/starterkit && ./deployment/deploy.sh   # Manual deploy
cd /opt/starterkit && ./deployment/rollback.sh # Rollback
docker compose restart api              # Restart API
./deployment/scripts/backup-db.sh       # Database backup
sudo certbot renew                      # Renew SSL
sudo ~/actions-runner/svc.sh status     # Runner status
```

---

## Time Estimate

| Phase | Task | Time |
|-------|------|------|
| 1 | AWS EC2 + DNS | ~15 min |
| 2 | Deploy user + SSH hardening | ~8 min |
| 3 | Server base setup (Docker, Nginx, fail2ban) | ~15 min |
| 4 | UFW Firewall | ~5 min |
| 5 | Project + secrets + env | ~10 min |
| 6 | Nginx configuration | ~10 min |
| 7 | Docker first launch | ~10 min |
| 8 | SSL with Certbot | ~5 min |
| 9 | GitHub runner | ~15 min |
| 10 | GitHub secrets | ~5 min |
| 11 | S3 backup (optional) | ~10 min |
| 12 | Script permissions | ~2 min |
| 13 | First CI/CD test | ~10 min |
| 14 | Security hardening (swap, IP whitelist, auto-updates) | ~10 min |
| 15 | Verification | ~5 min |
| | **Total** | **~2.5–3 hours** |

> DNS propagation may add 5–30 min. Route53/Cloudflare is nearly instant.
