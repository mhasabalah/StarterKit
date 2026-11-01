# Testing Production Docker Setup Locally

Test the complete production Docker Compose configuration before deploying to VPS.

---

## 🎯 What This Tests

This test simulates the **exact production environment** you'll deploy to VPS:

✅ **2 API instances** on ports 5000 and 5001 (ready for nginx load balancing)  
✅ **PostgreSQL** database  
✅ **Redis** cache  
✅ **Seq** logging  
✅ **Full observability stack** (Prometheus, Grafana, Jaeger, Tempo, Loki, OpenTelemetry)

**Note**: This does NOT include nginx - you'll configure that separately on your VPS.

---

## 🚀 Quick Start

### Run the Test

```powershell
.\test-production-docker.ps1
```

**Duration**: ~5 minutes (first run takes longer for Docker image build)

### What It Does

1. ✅ Validates Docker environment
2. ✅ Cleans up previous test runs
3. ✅ Builds production API image
4. ✅ Starts infrastructure (PostgreSQL, Redis, Seq)
5. ✅ Starts observability stack (Grafana, Prometheus, Jaeger)
6. ✅ Starts 2 API instances (ports 5000, 5001)
7. ✅ Waits for all services to be healthy
8. ✅ Tests health endpoints
9. ✅ Runs load test (100 requests)
10. ✅ Shows container status and access URLs

---

## 📊 Expected Results

After successful test, you'll see:

### API Endpoints
- http://localhost:5000/health ✅
- http://localhost:5001/health ✅
- http://localhost:5000/swagger ✅

### Observability Stack
- **Grafana**: http://localhost:3000 (admin/TestGrafana@2026)
- **Prometheus**: http://localhost:9090
- **Jaeger**: http://localhost:16686
- **Seq**: http://localhost:5341

### Container Status
All containers should show "Up" status:
```
starterkit-test-prod-api-1
starterkit-test-prod-api-2
starterkit-test-prod-postgres
starterkit-test-prod-redis
starterkit-test-prod-seq
starterkit-test-prod-prometheus
starterkit-test-prod-grafana
starterkit-test-prod-jaeger
starterkit-test-prod-otel-collector
... (and more observability services)
```

---

## 🧪 Manual Testing

After the script completes, test manually:

### Test Health Endpoints
```powershell
# API Instance 1
curl http://localhost:5000/health

# API Instance 2
curl http://localhost:5001/health
```

### Test Swagger UI
```powershell
# Open in browser
start http://localhost:5000/swagger
```

### Test Load Balancing
```powershell
# Send multiple requests
for ($i=1; $i -le 20; $i++) {
    $port = if ($i % 2 -eq 0) { 5001 } else { 5000 }
    curl -s "http://localhost:$port/health" | Out-Null
    Write-Host "Request $i sent to port $port"
}

# Check both APIs received requests
docker logs starterkit-test-prod-api-1 2>&1 | Select-String "GET /health" | Measure-Object
docker logs starterkit-test-prod-api-2 2>&1 | Select-String "GET /health" | Measure-Object
```

### Explore Observability

**Grafana Dashboards**:
```powershell
start http://localhost:3000
# Login: admin / TestGrafana@2026
```

**Prometheus Metrics**:
```powershell
start http://localhost:9090
# Query: rate(http_requests_total[5m])
```

**Jaeger Tracing**:
```powershell
start http://localhost:16686
# Search for traces from starterkit-api-1 and starterkit-api-2
```

**Seq Logs**:
```powershell
start http://localhost:5341
# View structured logs from all services
```

---

## 🐛 Troubleshooting

### Issue: Services fail to start

**Check logs**:
```powershell
# API logs
docker logs starterkit-test-prod-api-1 --tail 50
docker logs starterkit-test-prod-api-2 --tail 50

# Database logs
docker logs starterkit-test-prod-postgres --tail 50

# All services
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml logs
```

**Common causes**:
- Port conflict (5000, 5001, 5432, 6379, 3000, 9090 already in use)
- Insufficient memory (need ~4GB free)
- Docker not running

**Fix**:
```powershell
# Stop conflicting services
docker ps  # Check what's running

# Clean up and retry
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml --profile observability down
.\test-production-docker.ps1
```

### Issue: API returns 502 or connection refused

**Check**:
```powershell
# Is API container running?
docker ps | Select-String "api"

# Check API health
docker exec starterkit-test-prod-api-1 curl -f http://localhost:80/health
```

**Fix**:
```powershell
# Restart API
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml restart api api2

# Wait 30 seconds and test
Start-Sleep -Seconds 30
curl http://localhost:5000/health
```

### Issue: Database connection errors

**Check**:
```powershell
# Is PostgreSQL running?
docker ps | Select-String "postgres"

# Check PostgreSQL logs
docker logs starterkit-test-prod-postgres --tail 30
```

**Fix**:
```powershell
# Restart PostgreSQL
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml restart postgres

# Restart APIs after database is ready
Start-Sleep -Seconds 10
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml restart api api2
```

### Issue: Out of memory

**Check resources**:
```powershell
docker stats
```

**Fix**:
- Close other applications
- Increase Docker Desktop memory limit (Settings → Resources → Memory)
- Stop some observability services if needed:
  ```powershell
  docker stop starterkit-test-prod-jaeger starterkit-test-prod-tempo
  ```

---

## 📝 Configuration Files

### `.env.test-production`
Test environment variables (already configured with test values):
- PostgreSQL password: `TestProd@2026!SecurePassword`
- Redis password: `TestRedis@2026!SecurePassword`
- JWT secret: (long test key)
- Grafana admin: `admin/TestGrafana@2026`

**For VPS deployment**: Copy `deployment/.env.production.example` to `.env.production` and set real passwords.

### Docker Compose Files Used
1. `docker-compose.yml` - Base configuration
2. `deployment/docker-compose.production.yml` - Production overrides (2 API instances, resource limits)
3. `docker-compose.observability.yml` - Observability stack

---

## 🧹 Cleanup

### Stop All Services
```powershell
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml --profile observability down
```

### Remove All Data (full cleanup)
```powershell
docker compose --env-file .env.test-production -f docker-compose.yml -f deployment/docker-compose.production.yml --profile observability down -v

# This removes:
# - All containers
# - All volumes (database data, logs)
# - All networks
```

---

## 📊 Performance Expectations

### Resource Usage (Expected)
| Service | CPU | Memory |
|---------|-----|--------|
| API × 2 | 1.6 | ~1.2 GB |
| PostgreSQL | 1.0 | ~800 MB |
| Redis | 0.5 | ~400 MB |
| Seq | 0.5 | ~400 MB |
| Observability | 1.1 | ~800 MB |
| **Total** | **~5.0** | **~3.6 GB** |

### Response Times (Expected)
- Health check: <50ms
- API endpoints: <200ms
- Database queries: <100ms

If response times are much slower, check `docker stats` for resource constraints.

---

## ✅ Success Criteria

Before deploying to VPS, verify:

- [ ] All containers running (check with `docker ps`)
- [ ] Both API instances respond on ports 5000 and 5001
- [ ] Health checks return HTTP 200
- [ ] Swagger UI accessible
- [ ] PostgreSQL accessible (check logs, no errors)
- [ ] Redis accessible (check logs, no errors)
- [ ] Seq shows logs from all services
- [ ] Load test: >95% success rate
- [ ] Average response time <200ms
- [ ] Grafana shows metrics
- [ ] Jaeger shows traces
- [ ] No memory issues (check `docker stats`)

---

## 🚀 Next Steps: Deploy to VPS

Once local testing passes:

### 1. Prepare VPS
```bash
# On VPS (Ubuntu)
sudo apt update
sudo apt install -y docker.io docker-compose nginx
```

### 2. Clone Repository
```bash
git clone https://github.com/your-org/StarterKit.git
cd StarterKit
```

### 3. Configure Environment
```bash
# Copy and edit production environment
cp deployment/.env.production.example .env.production
nano .env.production  # Set real passwords
```

### 4. Deploy Docker Stack
```bash
# Start services (without nginx - nginx on host)
docker compose -f docker-compose.yml -f deployment/docker-compose.production.yml --profile observability up -d
```

### 5. Configure Nginx
```bash
# Copy nginx configurations
sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf
sudo cp deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit
sudo ln -s /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/

# Test and reload
sudo nginx -t
sudo systemctl reload nginx
```

### 6. Verify Deployment
```bash
# Check services
docker ps
curl http://localhost:5000/health
curl http://localhost:5001/health

# Test through nginx
curl http://YOUR_VPS_IP/health

# Check logs
docker logs starterkit-api-1 --tail 50
sudo tail -f /var/log/nginx/starterkit-access.log
```

---

## 📚 Documentation

- **Production Deployment Guide**: `wiki/deployment/Production-Deployment-Complete-Guide.md`
- **Nginx Configuration**: `wiki/deployment/README.Nginx.md`
- **Observability Stack**: `wiki/deployment/README.Observability.md`
- **Docker Compose**: `wiki/deployment/README.DockerCompose.md`

---

**Ready to test?** Run: `.\test-production-docker.ps1` 🚀
