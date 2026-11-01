# StarterKit Deployment Documentation

**Current Production**: api.example.com (AWS EC2, YOUR_SERVER_IP)

---

## 🚀 Quick Start

**For api.example.com deployment** (current production):

1. **[Production-Deployment.md](Production-Deployment.md)** ⭐
   - Complete 3-phase deployment (15-40 min)
   - DNS → Nginx/Docker → SSL
   - Troubleshooting & maintenance

2. **Verify deployment**: `curl https://api.example.com/health`

---

## 📚 Documentation Index

### Production Deployment
- **[Production-Deployment.md](Production-Deployment.md)** - Current production setup
- **[VPS-DEPLOYMENT-GUIDE.md](VPS-DEPLOYMENT-GUIDE.md)** - General VPS deployment
- **[Production-Deployment-Complete-Guide.md](Production-Deployment-Complete-Guide.md)** - Full AWS EC2 guide

### Infrastructure
- **[README.Nginx.md](README.Nginx.md)** - Nginx configuration reference
- **[README.DockerCompose.md](README.DockerCompose.md)** - Docker Compose guide
- **[README.Observability.md](README.Observability.md)** - Monitoring stack

### CI/CD
- **[GitHub-Actions-CI-CD.md](GitHub-Actions-CI-CD.md)** - GitHub Actions setup
- **[README.CICD.md](README.CICD.md)** - CI/CD pipeline reference

### Testing & Verification
- **[TESTING-PRODUCTION.md](TESTING-PRODUCTION.md)** - Production testing
- **[DEPLOYMENT-READINESS.md](DEPLOYMENT-READINESS.md)** - Pre-deployment checklist

---

## 🎯 Common Tasks

### Deploy to Production
```bash
cd /opt/starterkit
git pull origin master
docker-compose up -d --build
curl https://api.example.com/health
```

### View Logs
```bash
docker-compose logs -f api
sudo tail -f /var/log/nginx/starterkit-https-access.log
```

### Restart Services
```bash
docker-compose restart api
sudo systemctl reload nginx
```

### Check Status
```bash
docker-compose ps
curl https://api.example.com/health
```

---

## 🔗 Related Documentation

- [Main Wiki](../INDEX.md)
- [Architecture Overview](../Architecture/Overview.md)
- [Project README](../../README.md)

---

**Full Index**: [INDEX.md](INDEX.md)
