# StarterKit Production Deployment

**Current Production**: https://api.example.com
**Server**: AWS EC2 (YOUR_SERVER_IP)
**Runner**: Host-based self-hosted GitHub Actions runner
**Status**: ✅ Production Ready

> **📚 Complete Documentation**: [wiki/deployment/](../wiki/deployment/)

---

## 🚀 Quick Deploy

### Automated (Recommended)
```bash
# Push to master branch - triggers automatic deployment via GitHub Actions
git push origin master

# Or manually trigger from GitHub UI:
# Actions → Hybrid CI/CD → Run workflow
```

### Manual (Emergency Only)
```bash
# SSH to production server
ssh ubuntu@YOUR_SERVER_IP

# Navigate to project
cd /opt/starterkit

# Pull latest changes
git pull origin master

# Deploy
docker compose up -d --build

# Verify
curl https://api.example.com/health
```

---

## 📖 Documentation

### Production Deployment Guides

| Guide | Purpose |
|-------|---------|
| **[wiki/deployment/Production-Deployment.md](../wiki/deployment/Production-Deployment.md)** ⭐ | **Current production setup** (api.example.com) |
| **[wiki/deployment/VPS-RUNNER-SETUP.md](../wiki/deployment/VPS-RUNNER-SETUP.md)** 🚀 | **Host-based runner setup** (migrated 2026-02-02) |
| **[wiki/deployment/SEQ-SECURITY-SETUP.md](../wiki/deployment/SEQ-SECURITY-SETUP.md)** 🔒 | **Seq authentication setup** (SECURITY CRITICAL) |
| [wiki/deployment/GITHUB-RUNNER-SETUP.md](../wiki/deployment/GITHUB-RUNNER-SETUP.md) | GitHub configuration for self-hosted runner |
| [wiki/deployment/RUNNER-MAINTENANCE.md](../wiki/deployment/RUNNER-MAINTENANCE.md) | Runner maintenance and troubleshooting |
| [wiki/deployment/VPS-DEPLOYMENT-GUIDE.md](../wiki/deployment/VPS-DEPLOYMENT-GUIDE.md) | General VPS deployment guide |
| [wiki/deployment/README.Nginx.md](../wiki/deployment/README.Nginx.md) | Nginx configuration reference |
| [wiki/deployment/GitHub-Actions-CI-CD.md](../wiki/deployment/GitHub-Actions-CI-CD.md) | CI/CD setup with GitHub Actions |

### Full Index
👉 **[wiki/deployment/INDEX.md](../wiki/deployment/INDEX.md)** - Complete documentation catalog

---

## 📁 Files in This Folder

```
deployment/
├── nginx/                          # Nginx configuration
│   ├── nginx.conf                  # Main config
│   └── conf.d/
│       └── app-production.conf # Site config (api.example.com)
│
├── scripts/                        # Deployment scripts
│   ├── backup-db.sh
│   ├── restore-db.sh
│   └── health-check.sh
│
├── deploy.sh                       # Main deployment script
├── secrets-setup.sh                # Secrets configuration
├── secure-seq.sh                   # 🔒 Seq security setup (NEW)
├── .env.production.example         # Environment template
└── README.md                       # This file
```

---

## 🔧 Configuration Files

### Nginx Configuration
- **nginx.conf** - Main Nginx config (workers, rate limiting, SSL, upstreams)
- **conf.d/app-production.conf** - Site-specific config for api.example.com

### Environment
- **.env.production.example** - Template for production environment variables

---

## 📚 Related Documentation

- [wiki/deployment/](../wiki/deployment/) - All deployment documentation
- [wiki/Architecture/](../wiki/Architecture/) - System architecture
- [wiki/INDEX.md](../wiki/INDEX.md) - Main documentation index

---

**Production URLs**:
- API: https://api.example.com
- Health: https://api.example.com/health
- Swagger: https://api.example.com/swagger (IP restricted)
- Seq: https://api.example.com/seq/ (IP restricted)
