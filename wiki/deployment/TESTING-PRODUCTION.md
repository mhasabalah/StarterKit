# Production Testing Guide
## How to Test StarterKit in Production Mode

---

## 🎯 Quick Answer

To test production configuration:

```bash
# Option 1: Test with production config + self-signed SSL
docker-compose --profile prod up -d

# Option 2: Test with production nginx config overlay
docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml up -d

# Option 3: Full production stack (recommended for staging)
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

---

## 📊 Understanding Your Docker Compose Files

Your project has **4 docker-compose files**:

### 1. `docker-compose.yml` (Base Configuration)
**What it has:**
- ✅ All core services (API, Postgres, Redis, Seq)
- ✅ Development settings by default
- ✅ Nginx with profile flag (`--profile nginx`)

**When to use:** Always! This is the base file.

### 2. `docker-compose.override.yml` (Development Overrides)
**What it changes:**
- Development-specific settings
- Automatically applied when you run `docker-compose up`

**When to use:** Automatic for development

### 3. `docker-compose.prod.yml` (Production Overrides)
**What it changes:**
- Resource limits (CPU, memory)
- Replicas (3 API instances for load balancing)
- Production environment variables
- Restart policies

**When to use:** Production deployment

### 4. `docker-compose.nginx-prod.yml` (Nginx Production Config)
**What it changes:**
- Uses `app.conf` (HTTPS + security)
- Mounts Let's Encrypt certificates
- Adds Certbot for auto-renewal
- Production optimizations

**When to use:** Testing nginx production config

---

## 🧪 Testing Scenarios

### Scenario 1: Test Production Nginx Config (with Self-Signed SSL)

**Use Case:** You want to test HTTPS and production nginx settings locally

```bash
# Step 1: Stop current services
docker-compose down

# Step 2: Create test production config with self-signed certs
cp ./infra/nginx/conf.d/app.conf ./infra/nginx/conf.d/app-test.conf

# Step 3: Edit app-test.conf to use self-signed certs
# Change these lines:
#   FROM: ssl_certificate /etc/letsencrypt/live/${NGINX_SERVER_NAME}/fullchain.pem;
#   TO:   ssl_certificate /etc/nginx/ssl/cert.pem;
#
#   FROM: ssl_certificate_key /etc/letsencrypt/live/${NGINX_SERVER_NAME}/privkey.pem;
#   TO:   ssl_certificate_key /etc/nginx/ssl/key.pem;

# Using PowerShell to edit:
$config = Get-Content ./infra/nginx/conf.d/app.conf -Raw
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/fullchain\.pem', '/etc/nginx/ssl/cert.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/privkey\.pem', '/etc/nginx/ssl/key.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/chain\.pem', '/etc/nginx/ssl/cert.pem'
$config | Out-File -FilePath ./infra/nginx/conf.d/app-test.conf -Encoding UTF8

# Step 4: Update .env
echo "NGINX_CONFIG_FILE=app-test.conf" >> .env

# Step 5: Start services
docker-compose --profile nginx up -d

# Step 6: Test HTTPS (ignore self-signed cert warning)
curl -k https://localhost/health
curl -k https://localhost/alive

# Step 7: Test HTTP to HTTPS redirect
curl -I http://localhost/api/something
# Should return: 301 Moved Permanently, Location: https://...
```

---

### Scenario 2: Test Full Production Stack (Staging Environment)

**Use Case:** Simulate complete production setup with all optimizations

```bash
# Step 1: Stop current services
docker-compose down

# Step 2: Use production compose file
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# What this does:
# - Runs 3 API instances (load balanced)
# - Sets production resource limits
# - Uses production restart policies
# - Applies production environment settings

# Step 3: Verify multiple API instances
docker-compose ps
# Should see: starterkit-api_1, starterkit-api_2, starterkit-api_3

# Step 4: Test load balancing
for i in {1..10}; do curl http://localhost/health; done
# Each request may hit different API instance

# Step 5: Check resource limits
docker stats
```

---

### Scenario 3: Test Production Nginx + Certbot (Full Integration Test)

**Use Case:** Test complete production setup with Certbot integration, SSL, and auto-renewal

**What This Tests:**
- ✅ Production nginx config with HTTPS
- ✅ Certbot container integration
- ✅ Self-signed certificates for testing
- ✅ Certificate auto-renewal simulation
- ✅ HTTP to HTTPS redirects
- ✅ Security headers validation
- ✅ Load balancing (if using prod.yml)

#### Step-by-Step Instructions

**Prerequisites:**
```powershell
# Ensure you're in project root
cd E:\real\test pro\StarterKit

# Stop any running services
docker-compose down -v
```

#### Step 1: Create Test Production Config

Create a production config that uses self-signed certificates instead of Let's Encrypt:

```powershell
# Run this PowerShell script
$config = Get-Content ./infra/nginx/conf.d/app.conf -Raw

# Replace Let's Encrypt paths with self-signed cert paths
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/fullchain\.pem', '/etc/nginx/ssl/cert.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/privkey\.pem', '/etc/nginx/ssl/key.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/chain\.pem', '/etc/nginx/ssl/cert.pem'

# Save as test config
$config | Out-File -FilePath ./infra/nginx/conf.d/app-test.conf -Encoding UTF8 -NoNewline

Write-Host "✅ Created: ./infra/nginx/conf.d/app-test.conf" -ForegroundColor Green
```

Or use the automated script:
```powershell
.\create-test-prod-config.ps1
```

#### Step 2: Update Environment Variables

```powershell
# Create/update .env file for testing
@"
NGINX_SERVER_NAME=localhost
NGINX_CONFIG_FILE=app-test.conf
ASPNETCORE_ENVIRONMENT=Staging
COMPOSE_PROJECT_NAME=starterkit-test
"@ | Out-File -FilePath .env -Encoding UTF8
```

#### Step 3: Start Production Stack with Certbot

**Option A: Nginx + Certbot Only**
```powershell
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.nginx-prod.yml `
  --profile prod `
  up -d
```

**Option B: Full Production Stack (Recommended)**
```powershell
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.prod.yml `
  -f docker-compose.nginx-prod.yml `
  --profile prod `
  up -d
```

#### Step 4: Verify Services Started

```powershell
# Check all services are running
docker-compose ps

# Expected output:
# NAME                    STATUS      PORTS
# starterkit-nginx          running     0.0.0.0:80->80, 0.0.0.0:443->443
# starterkit-api            running
# starterkit-postgres       running
# starterkit-redis          running
# starterkit-seq            running
# starterkit-certbot        running

# Check logs for errors
docker-compose logs nginx --tail=50
docker-compose logs certbot --tail=20
```

#### Step 5: Test HTTP Endpoints

```powershell
# Test basic HTTP health endpoints
Write-Host "`n=== Testing HTTP Endpoints ===" -ForegroundColor Cyan

$healthHttp = Invoke-WebRequest -Uri "http://localhost/health" -UseBasicParsing
Write-Host "HTTP /health: $($healthHttp.StatusCode) - $($healthHttp.Content)" -ForegroundColor Green

$aliveHttp = Invoke-WebRequest -Uri "http://localhost/alive" -UseBasicParsing
Write-Host "HTTP /alive: $($aliveHttp.StatusCode) - $($aliveHttp.Content)" -ForegroundColor Green
```

#### Step 6: Test HTTPS Endpoints

```powershell
# Test HTTPS with self-signed certificates (skip validation)
Write-Host "`n=== Testing HTTPS Endpoints ===" -ForegroundColor Cyan

# Skip certificate validation for self-signed certs
if (-not ([System.Management.Automation.PSTypeName]'ServerCertificateValidationCallback').Type) {
    $certCallback = @"
        using System.Net;
        using System.Net.Security;
        using System.Security.Cryptography.X509Certificates;
        public class ServerCertificateValidationCallback {
            public static void Ignore() {
                ServicePointManager.ServerCertificateValidationCallback += 
                    delegate (object obj, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) { return true; };
            }
        }
"@
    Add-Type $certCallback
}
[ServerCertificateValidationCallback]::Ignore()

$healthHttps = Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing
Write-Host "HTTPS /health: $($healthHttps.StatusCode) - $($healthHttps.Content)" -ForegroundColor Green

$aliveHttps = Invoke-WebRequest -Uri "https://localhost/alive" -UseBasicParsing
Write-Host "HTTPS /alive: $($aliveHttps.StatusCode) - $($aliveHttps.Content)" -ForegroundColor Green
```

#### Step 7: Test HTTP to HTTPS Redirect

```powershell
Write-Host "`n=== Testing HTTP to HTTPS Redirect ===" -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest -Uri "http://localhost/api/test" `
        -MaximumRedirection 0 `
        -ErrorAction Stop
} catch {
    if ($_.Exception.Response.StatusCode -eq 301) {
        $location = $_.Exception.Response.Headers['Location']
        Write-Host "✅ Redirect: 301 → $location" -ForegroundColor Green
    } else {
        Write-Host "❌ Unexpected response: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}
```

#### Step 8: Test Security Headers

```powershell
Write-Host "`n=== Validating Security Headers ===" -ForegroundColor Cyan

$response = Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing
$headers = $response.Headers

$securityHeaders = @(
    'Strict-Transport-Security',
    'X-Frame-Options',
    'X-Content-Type-Options',
    'X-XSS-Protection',
    'Content-Security-Policy',
    'Referrer-Policy',
    'Permissions-Policy'
)

foreach ($header in $securityHeaders) {
    if ($headers.ContainsKey($header)) {
        Write-Host "✅ $header : $($headers[$header])" -ForegroundColor Green
    } else {
        Write-Host "❌ $header : Missing" -ForegroundColor Red
    }
}
```

#### Step 9: Test Certbot Integration

```powershell
Write-Host "`n=== Testing Certbot ===" -ForegroundColor Cyan

# Check Certbot container is running
$certbotStatus = docker ps --filter "name=starterkit-certbot" --format "{{.Status}}"
Write-Host "Certbot Status: $certbotStatus" -ForegroundColor $(if ($certbotStatus -like "*Up*") { "Green" } else { "Red" })

# Test certificate renewal (dry-run)
Write-Host "`nSimulating certificate renewal..." -ForegroundColor Yellow
docker-compose exec certbot certbot renew --dry-run --quiet 2>&1 | Write-Host
```

#### Step 10: Inspect Nginx Configuration

```powershell
Write-Host "`n=== Nginx Configuration Validation ===" -ForegroundColor Cyan

# Test nginx config syntax
docker-compose exec nginx nginx -t

# Show SSL configuration
Write-Host "`nSSL Certificate Paths:" -ForegroundColor Yellow
docker-compose exec nginx nginx -T 2>&1 | Select-String "ssl_certificate"

# Show upstream configuration
Write-Host "`nUpstream Configuration:" -ForegroundColor Yellow
docker-compose exec nginx nginx -T 2>&1 | Select-String "upstream" -Context 0,5
```

#### Step 11: Load Testing (Optional)

```powershell
Write-Host "`n=== Load Testing ===" -ForegroundColor Cyan

# Simple load test with PowerShell
$requests = 100
$concurrent = 10
$results = @()

Write-Host "Running $requests requests with $concurrent concurrent connections..." -ForegroundColor Yellow

1..$requests | ForEach-Object -Parallel {
    $start = Get-Date
    try {
        $response = Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing
        $duration = (Get-Date) - $start
        [PSCustomObject]@{
            Success = $true
            Duration = $duration.TotalMilliseconds
            StatusCode = $response.StatusCode
        }
    } catch {
        [PSCustomObject]@{
            Success = $false
            Duration = -1
            StatusCode = 0
        }
    }
} -ThrottleLimit $concurrent | Tee-Object -Variable results

# Calculate statistics
$successful = ($results | Where-Object Success).Count
$avgTime = ($results | Where-Object Success | Measure-Object -Property Duration -Average).Average
$minTime = ($results | Where-Object Success | Measure-Object -Property Duration -Minimum).Minimum
$maxTime = ($results | Where-Object Success | Measure-Object -Property Duration -Maximum).Maximum

Write-Host "`n--- Results ---" -ForegroundColor Cyan
Write-Host "Total Requests: $requests"
Write-Host "Successful: $successful ($([math]::Round($successful/$requests*100, 2))%)"
Write-Host "Avg Response Time: $([math]::Round($avgTime, 2))ms"
Write-Host "Min Response Time: $([math]::Round($minTime, 2))ms"
Write-Host "Max Response Time: $([math]::Round($maxTime, 2))ms"
```

#### Step 12: Certificate Details

```powershell
Write-Host "`n=== SSL Certificate Information ===" -ForegroundColor Cyan

# Show certificate details
docker exec starterkit-nginx openssl x509 `
    -in /etc/nginx/ssl/cert.pem `
    -noout `
    -subject `
    -issuer `
    -dates `
    -fingerprint

# Check certificate expiration
$certInfo = docker exec starterkit-nginx openssl x509 `
    -in /etc/nginx/ssl/cert.pem `
    -noout `
    -enddate

Write-Host "`n$certInfo" -ForegroundColor $(if ($certInfo -match "2027") { "Green" } else { "Yellow" })
```

#### Step 13: Cleanup

```powershell
# When done testing, stop services
Write-Host "`n=== Cleanup ===" -ForegroundColor Cyan
docker-compose down

# Or keep running for further testing
# docker-compose logs -f nginx
```

---

### 🎯 Automated Test Script

Save this as `test-scenario3.ps1`:

```powershell
# Test Scenario 3: Production Nginx + Certbot
# Complete integration test

Write-Host @"
╔══════════════════════════════════════════════════════════╗
║  Scenario 3: Production Nginx + Certbot Integration Test  ║
╚══════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

# Step 1: Create test config
Write-Host "`n[1/13] Creating test production config..." -ForegroundColor Yellow
if (!(Test-Path "./infra/nginx/conf.d/app-test.conf")) {
    $config = Get-Content ./infra/nginx/conf.d/app.conf -Raw
    $config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/fullchain\.pem', '/etc/nginx/ssl/cert.pem'
    $config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/privkey\.pem', '/etc/nginx/ssl/key.pem'
    $config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/chain\.pem', '/etc/nginx/ssl/cert.pem'
    $config | Out-File -FilePath ./infra/nginx/conf.d/app-test.conf -Encoding UTF8 -NoNewline
    Write-Host "✅ Config created" -ForegroundColor Green
} else {
    Write-Host "✅ Config already exists" -ForegroundColor Green
}

# Step 2: Set environment
Write-Host "`n[2/13] Setting environment variables..." -ForegroundColor Yellow
@"
NGINX_SERVER_NAME=localhost
NGINX_CONFIG_FILE=app-test.conf
ASPNETCORE_ENVIRONMENT=Staging
COMPOSE_PROJECT_NAME=starterkit-test
"@ | Out-File -FilePath .env -Encoding UTF8
Write-Host "✅ Environment configured" -ForegroundColor Green

# Step 3: Start services
Write-Host "`n[3/13] Starting production services..." -ForegroundColor Yellow
docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml --profile prod up -d
Start-Sleep -Seconds 10
Write-Host "✅ Services started" -ForegroundColor Green

# Step 4: Verify services
Write-Host "`n[4/13] Verifying services..." -ForegroundColor Yellow
$services = docker-compose ps --services
Write-Host "Running services: $($services -join ', ')" -ForegroundColor Green

# Step 5-12: Run tests (HTTP, HTTPS, redirects, headers, etc.)
Write-Host "`n[5/13] Testing HTTP endpoints..." -ForegroundColor Yellow
try {
    $health = Invoke-WebRequest -Uri "http://localhost/health" -UseBasicParsing -TimeoutSec 5
    Write-Host "✅ HTTP /health: $($health.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "❌ HTTP /health failed: $_" -ForegroundColor Red
}

Write-Host "`n[6/13] Testing HTTPS endpoints..." -ForegroundColor Yellow
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
try {
    $healthHttps = Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing -TimeoutSec 5
    Write-Host "✅ HTTPS /health: $($healthHttps.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "❌ HTTPS /health failed: $_" -ForegroundColor Red
}

Write-Host "`n[7/13] Testing HTTP→HTTPS redirect..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri "http://localhost/api/test" -MaximumRedirection 0 -ErrorAction Stop
} catch {
    if ($_.Exception.Response.StatusCode -eq 301) {
        Write-Host "✅ Redirect working: 301" -ForegroundColor Green
    }
}

Write-Host "`n[8/13] Validating security headers..." -ForegroundColor Yellow
$response = Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing
$requiredHeaders = @('Strict-Transport-Security', 'X-Frame-Options', 'X-Content-Type-Options')
foreach ($header in $requiredHeaders) {
    if ($response.Headers.ContainsKey($header)) {
        Write-Host "✅ $header present" -ForegroundColor Green
    } else {
        Write-Host "❌ $header missing" -ForegroundColor Red
    }
}

Write-Host "`n[9/13] Testing Certbot..." -ForegroundColor Yellow
$certbotRunning = docker ps --filter "name=certbot" --format "{{.Names}}"
if ($certbotRunning) {
    Write-Host "✅ Certbot container running: $certbotRunning" -ForegroundColor Green
} else {
    Write-Host "⚠️  Certbot container not found" -ForegroundColor Yellow
}

Write-Host "`n[10/13] Validating Nginx config..." -ForegroundColor Yellow
docker-compose exec nginx nginx -t 2>&1 | Write-Host

Write-Host "`n[11/13] Checking SSL certificate..." -ForegroundColor Yellow
docker exec starterkit-test-nginx openssl x509 -in /etc/nginx/ssl/cert.pem -noout -dates | Write-Host

Write-Host "`n[12/13] Performance test..." -ForegroundColor Yellow
$iterations = 50
$start = Get-Date
1..$iterations | ForEach-Object {
    Invoke-WebRequest -Uri "https://localhost/health" -UseBasicParsing | Out-Null
}
$duration = (Get-Date) - $start
$avgMs = [math]::Round(($duration.TotalMilliseconds / $iterations), 2)
Write-Host "✅ Avg response time: ${avgMs}ms ($iterations requests)" -ForegroundColor Green

Write-Host "`n[13/13] Test Summary" -ForegroundColor Cyan
Write-Host @"
═══════════════════════════════════════════
  ✅ All tests completed!
  
  Production features tested:
  • HTTPS with SSL certificates
  • HTTP to HTTPS redirects
  • Security headers
  • Certbot integration
  • Nginx configuration
  • Performance baseline
  
  Next steps:
  • Review logs: docker-compose logs nginx
  • Stop services: docker-compose down
═══════════════════════════════════════════
"@ -ForegroundColor Green
```

Run it with:
```powershell
.\test-scenario3.ps1
```

---

### Scenario 4: Complete Production Simulation

**Use Case:** Full production test including HTTPS, load balancing, and auto-renewal

```bash
# Step 1: Prepare environment
cat > .env.prod << 'EOF'
COMPOSE_PROJECT_NAME=starterkit-prod
ASPNETCORE_ENVIRONMENT=Production
NGINX_SERVER_NAME=localhost
NGINX_CONFIG_FILE=app-test.conf
API_REPLICAS=3
EOF

# Step 2: Use production env file
cp .env.prod .env

# Step 3: Start all production services
docker-compose \
  -f docker-compose.yml \
  -f docker-compose.prod.yml \
  -f docker-compose.nginx-prod.yml \
  --profile prod \
  up -d

# Step 4: Comprehensive tests
./test-production-ssl.ps1
```

---

## 📋 Pre-Production Checklist

Before deploying to production, test these:

```bash
# ✅ 1. Health Endpoints
curl http://localhost/health    # Should return: Healthy
curl http://localhost/alive     # Should return: Healthy

# ✅ 2. HTTPS Endpoints (with test config)
curl -k https://localhost/health
curl -k https://localhost/alive

# ✅ 3. HTTP to HTTPS Redirect
curl -I http://localhost/api
# Should return: 301 Moved Permanently

# ✅ 4. Security Headers
curl -I https://localhost/health -k | grep -E "(Strict-Transport-Security|X-Frame-Options|Content-Security-Policy)"

# ✅ 5. API Endpoints
curl http://localhost/api/v1/identity/health
curl http://localhost/swagger  # Should return 404 in prod

# ✅ 6. Load Balancing (if multiple replicas)
for i in {1..20}; do curl http://localhost/health; sleep 0.1; done

# ✅ 7. Rate Limiting
for i in {1..200}; do curl http://localhost/health; done
# Should eventually return 429 Too Many Requests

# ✅ 8. Database Connection
docker-compose exec api dotnet ef database update --no-build

# ✅ 9. Redis Connection
docker-compose exec redis redis-cli -a ${REDIS_PASSWORD} ping
# Should return: PONG

# ✅ 10. Logs
docker-compose logs api | tail -20
docker-compose logs nginx | tail -20
```

---

## 🚀 Recommended Testing Flow

### For Local Testing (Developer Machine)

```bash
# Start with dev tools
docker-compose --profile dev --profile nginx up -d

# Test HTTP endpoints
curl http://localhost/health
curl http://localhost/alive
curl http://localhost/swagger
```

### For Staging Environment (Pre-Production Server)

```bash
# 1. Create test production config
./create-test-prod-config.ps1  # (Script provided below)

# 2. Start with production settings
docker-compose --profile prod up -d

# 3. Run production tests
./test-production-ssl.ps1

# 4. Load test
ab -n 1000 -c 10 http://localhost/health
```

### For Production Environment (Live Server)

```bash
# 1. Obtain real SSL certificate
docker run --rm \
  -v /etc/letsencrypt:/etc/letsencrypt \
  -v $(pwd)/infra/nginx/certbot:/var/www/certbot \
  certbot/certbot certonly --webroot \
  -w /var/www/certbot \
  -d api.yourdomain.com \
  --email admin@yourdomain.com \
  --agree-tos

# 2. Uncomment SSL volume mount in docker-compose.yml
# Line 307: - /etc/letsencrypt:/etc/letsencrypt:ro

# 3. Set production environment
cat > .env << 'EOF'
NGINX_SERVER_NAME=api.yourdomain.com
NGINX_CONFIG_FILE=app.conf
ASPNETCORE_ENVIRONMENT=Production
EOF

# 4. Deploy
docker-compose --profile prod up -d

# 5. Verify
curl https://api.yourdomain.com/health
curl https://api.yourdomain.com/alive
```

---

## 🛠️ Helper Scripts

### Script: `create-test-prod-config.ps1`

```powershell
# Creates test production config with self-signed certs
Write-Host "Creating test production config..." -ForegroundColor Cyan

$config = Get-Content ./infra/nginx/conf.d/app.conf -Raw

# Replace Let's Encrypt paths with self-signed cert paths
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/fullchain\.pem', '/etc/nginx/ssl/cert.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/privkey\.pem', '/etc/nginx/ssl/key.pem'
$config = $config -replace '/etc/letsencrypt/live/\$\{NGINX_SERVER_NAME\}/chain\.pem', '/etc/nginx/ssl/cert.pem'

# Save as test config
$config | Out-File -FilePath ./infra/nginx/conf.d/app-test.conf -Encoding UTF8

Write-Host "✅ Created: ./infra/nginx/conf.d/app-test.conf" -ForegroundColor Green
Write-Host "Set NGINX_CONFIG_FILE=app-test.conf in .env" -ForegroundColor Yellow
```

---

## 📊 Comparison Table

| Feature | Development | Staging/Test Production | Production |
|---------|------------|------------------------|------------|
| **Command** | `docker-compose up -d` | `docker-compose --profile prod up -d` | `docker-compose --profile prod up -d` |
| **Config File** | `app-dev.conf` | `app-test.conf` | `app.conf` |
| **SSL** | None (HTTP only) | Self-signed | Let's Encrypt |
| **HTTPS** | ❌ | ✅ | ✅ |
| **API Replicas** | 1 | 1-3 | 3+ |
| **Rate Limiting** | Relaxed | Production | Production |
| **Swagger** | ✅ Enabled | ✅ Enabled | ❌ Disabled |
| **Security Headers** | Basic | Full | Full |
| **Certbot** | ❌ | ❌ | ✅ Auto-renewal |
| **Environment** | `Development` | `Staging` | `Production` |

---

## 🎯 Quick Commands Reference

```bash
# Stop everything
docker-compose down

# Development mode
docker-compose --profile dev up -d

# Production test (HTTP)
docker-compose --profile prod up -d

# Production test (HTTPS with self-signed)
# (After creating app-test.conf)
NGINX_CONFIG_FILE=app-test.conf docker-compose --profile prod up -d

# Full production stack
docker-compose -f docker-compose.yml -f docker-compose.prod.yml --profile prod up -d

# With nginx prod config
docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml --profile prod up -d

# Complete production simulation
docker-compose \
  -f docker-compose.yml \
  -f docker-compose.prod.yml \
  -f docker-compose.nginx-prod.yml \
  --profile prod \
  up -d

# View logs
docker-compose logs -f nginx
docker-compose logs -f api

# Check status
docker-compose ps
docker stats

# Run tests
./test-production-ssl.ps1
```

---

## ✅ Summary

**For Testing Production Config:**

1. **Simple HTTP Test:**
   ```bash
   docker-compose --profile prod up -d
   ```

2. **HTTPS Test (Self-Signed):**
   ```bash
   # Create test config first
   ./create-test-prod-config.ps1
   NGINX_CONFIG_FILE=app-test.conf docker-compose --profile prod up -d
   ```

3. **Full Production Simulation:**
   ```bash
   docker-compose -f docker-compose.yml -f docker-compose.prod.yml --profile prod up -d
   ```

**The key difference:**
- `--profile prod` = Uses production nginx config and enables certbot
- `-f docker-compose.prod.yml` = Adds production resource limits and replicas
- `app-test.conf` = Production HTTPS config with self-signed certs for testing

---

**Created:** 2026-01-20  
**Use this guide** to test before production deployment!
