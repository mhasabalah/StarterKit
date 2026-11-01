# Production Deployment Guide - api.example.com

**Domain**: api.example.com | **Server**: AWS EC2 (YOUR_SERVER_IP)
**Status**: Production Ready | **SSL Grade**: A+

---

## Quick Deployment (15-40 minutes)

### Prerequisites
✅ Domain: **api.example.com** (purchased and ready)
✅ AWS EC2: Ubuntu 22.04 LTS, t3.medium, IP YOUR_SERVER_IP
✅ SSH access configured
✅ Git repository accessible

### Three-Phase Deployment

```bash
# Phase 1: DNS Configuration (5-30 min, depends on propagation)
# Phase 2: Nginx + Docker Deployment (5 min)
# Phase 3: SSL/HTTPS Activation (2 min)
```

---

## Phase 1: DNS Configuration

### Configure Domain Records

**Go to your domain registrar** and add these DNS records:

```
Record Type: A
Name: @
Value: YOUR_SERVER_IP
TTL: 300

Record Type: A
Name: www
Value: YOUR_SERVER_IP
TTL: 300
```

### Verify DNS Propagation

```bash
# Wait 5-30 minutes, then check:
dig +short api.example.com
# Expected: YOUR_SERVER_IP

# Check globally:
# https://dnschecker.org/#A/api.example.com
```

**⚠️ Do not proceed** until DNS returns the correct IP!

---

## Phase 2: Nginx + Docker Deployment

### Step 1: SSH to Server

```bash
ssh -i your-key.pem ubuntu@YOUR_SERVER_IP
```

### Step 2: Pull Latest Configuration

```bash
cd /opt/starterkit
git pull origin master
```

### Step 3: Install Dependencies (First Time Only)

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install Nginx
sudo apt install nginx -y

# Install Certbot for SSL
sudo apt install certbot python3-certbot-nginx -y

# Verify Docker & Docker Compose installed
docker --version
docker-compose --version
```

### Step 4: Deploy Nginx Configuration

```bash
# Create certbot webroot
sudo mkdir -p /var/www/certbot
sudo chown -R www-data:www-data /var/www/certbot

# Deploy main Nginx config
sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf

# Deploy site-specific config
sudo cp deployment/nginx/conf.d/app-production.conf \
    /etc/nginx/sites-available/starterkit

# Remove default site
sudo rm -f /etc/nginx/sites-enabled/default

# Enable StarterKit site
sudo ln -sf /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/starterkit

# Test configuration
sudo nginx -t
# Expected: "syntax is ok" and "test is successful"

# Reload Nginx
sudo systemctl reload nginx
sudo systemctl enable nginx
```

### Step 5: Start Docker Services

```bash
cd /opt/starterkit

# Ensure secrets exist (production environment)
ls -la secrets/

# Start all services
docker-compose up -d

# Verify all containers running
docker-compose ps
# Expected: All services "Up"

# Check API health
curl http://localhost:5000/health
curl http://localhost:5001/health
# Expected: HTTP 200 OK
```

### Step 6: Verify HTTP Access

```bash
# Test from server
curl -I http://api.example.com/health

# Test from your local machine
curl -I http://api.example.com/health

# Expected: HTTP/1.1 301 Moved Permanently (redirect to HTTPS)
# or HTTP/1.1 200 OK (if SSL not yet configured)
```

---

## Phase 3: SSL/HTTPS Activation

### Obtain SSL Certificate (Automatic)

```bash
# Run Certbot (it will auto-configure everything)
sudo certbot --nginx -d api.example.com

# Follow the prompts:
# Email: your-email@example.com
# Agree to Terms: Yes
# Share email with EFF: No (optional)
# Redirect HTTP→HTTPS: Yes (recommended)
```

**What Certbot Does**:
1. ✅ Obtains SSL certificate from Let's Encrypt (free, trusted)
2. ✅ Updates Nginx configuration with SSL paths
3. ✅ Enables HTTPS server block
4. ✅ Configures HTTP→HTTPS redirect
5. ✅ Sets up automatic renewal (90-day cert, renews at 60 days)

### Verify HTTPS Working

```bash
# Test HTTPS
curl -I https://api.example.com/health
# Expected: HTTP/2 200 OK

# Test HTTP redirect
curl -I http://api.example.com/health
# Expected: HTTP/1.1 301 → https://api.example.com/health

# Check certificate
echo | openssl s_client -connect api.example.com:443 -servername api.example.com 2>/dev/null | \
    openssl x509 -noout -dates
# Expected: Valid dates, expires in ~90 days

# Verify auto-renewal
sudo certbot renew --dry-run
# Expected: "all simulated renewals succeeded"
```

### Test SSL Rating

**Visit**: https://www.ssllabs.com/ssltest/analyze.html?d=api.example.com

**Expected Grade**: A or A+ ✅

---

## Post-Deployment: Security Configuration

### Enable Monitoring Access (IP Whitelist)

Monitoring dashboards are **blocked by default** for security.

```bash
# 1. Get your IP address
curl ifconfig.me
# Example: 203.0.113.45

# 2. Edit Nginx config
sudo nano /etc/nginx/sites-available/starterkit

# 3. Find monitoring sections and add your IP:

# Seq Logs
location /seq/ {
    allow 203.0.113.45;  # ← Add your IP here
    deny all;
    # ... rest of config
}

# Swagger API Docs
location /swagger {
    allow 203.0.113.45;  # ← Add your IP here
    deny all;
    # ... rest of config
}

# Repeat for /grafana/, /prometheus/, /jaeger/

# 4. Save and reload
sudo nginx -t
sudo systemctl reload nginx
```

### Access Monitoring Dashboards

After adding your IP, you can access:

- **Seq Logs**: https://api.example.com/seq/
- **Swagger API**: https://api.example.com/swagger/
- **Grafana** (if deployed): https://api.example.com/grafana/

---

## Production Verification

### 1. Health Check

```bash
curl https://api.example.com/health
```

**Expected**:
```json
{"status":"Healthy"}
```

### 2. Security Headers

```bash
curl -I https://api.example.com/health
```

**Must include**:
```
strict-transport-security: max-age=63072000; includeSubDomains
x-frame-options: DENY
x-content-type-options: nosniff
x-xss-protection: 1; mode=block
```

### 3. Rate Limiting

```bash
# Send 150 rapid requests
for i in {1..150}; do
    curl -s -o /dev/null -w "%{http_code}\n" https://api.example.com/health
done
```

**Expected**: Mix of 200 (OK) and 429 (Too Many Requests)

### 4. Load Balancing

```bash
# Check both API instances responding
docker-compose logs api | grep "listening"

# Test load distribution (check logs while making requests)
for i in {1..20}; do
    curl -s https://api.example.com/health > /dev/null
done

# Check Nginx upstream status
curl http://127.0.0.1/nginx_status
```

### 5. SSL Certificate

```bash
# Check expiry
echo | openssl s_client -connect api.example.com:443 2>/dev/null | \
    openssl x509 -noout -dates

# Check renewal timer
sudo systemctl status certbot.timer
```

---

## Production URLs

### Public Endpoints
- **API Base**: https://api.example.com
- **Health Check**: https://api.example.com/health
- **Alive Check**: https://api.example.com/alive
- **API Documentation**: https://api.example.com/swagger (IP restricted)

### Monitoring (IP Restricted)
- **Seq Logs**: https://api.example.com/seq/
- **Grafana Dashboards**: https://api.example.com/grafana/
- **Prometheus Metrics**: https://api.example.com/prometheus/
- **Jaeger Tracing**: https://api.example.com/jaeger/

### Server Management
- **SSH**: `ssh -i key.pem ubuntu@YOUR_SERVER_IP`
- **Nginx Status**: http://127.0.0.1/nginx_status (localhost only)

---

## Monitoring & Logs

### View Logs

```bash
# Nginx HTTPS access logs
sudo tail -f /var/log/nginx/starterkit-https-access.log

# Nginx error logs
sudo tail -f /var/log/nginx/starterkit-https-error.log

# API application logs
docker-compose logs -f api

# All Docker services
docker-compose logs -f

# Specific service
docker-compose logs -f postgres
docker-compose logs -f redis
docker-compose logs -f seq
```

### Check Service Status

```bash
# Nginx
sudo systemctl status nginx

# Docker containers
docker-compose ps

# API health endpoints
curl https://api.example.com/health
curl https://api.example.com/alive

# Container stats
docker stats
```

### Log Analysis

```bash
# Count HTTP status codes
sudo awk '{print $9}' /var/log/nginx/starterkit-https-access.log | \
    sort | uniq -c | sort -rn

# Top 20 IP addresses
sudo awk '{print $1}' /var/log/nginx/starterkit-https-access.log | \
    sort | uniq -c | sort -rn | head -20

# Find slow requests (>1 second)
sudo grep -E 'request_time":[1-9]' /var/log/nginx/starterkit-https-access.log

# Check rate limit hits
sudo grep "limiting requests" /var/log/nginx/starterkit-https-error.log
```

---

## Troubleshooting

### Issue: DNS Not Resolving

```bash
# Symptom: dig api.example.com returns nothing or wrong IP

# Solution:
# 1. Wait for DNS propagation (5-30 minutes, up to 48 hours)
# 2. Clear local DNS cache:
#    Windows: ipconfig /flushdns
#    Mac: sudo dscacheutil -flushcache
#    Linux: sudo systemd-resolve --flush-caches
# 3. Check from different DNS: dig @8.8.8.8 api.example.com
# 4. Verify domain registrar settings
```

### Issue: 502 Bad Gateway

```bash
# Symptom: Nginx returns 502

# Check API containers
docker-compose ps
# Expected: All "Up"

# Check API responding
curl http://localhost:5000/health
curl http://localhost:5001/health

# Check logs
docker-compose logs api | tail -50
sudo tail -50 /var/log/nginx/starterkit-https-error.log

# Solution: Restart API
docker-compose restart api
```

### Issue: SSL Certificate Error

```bash
# Symptom: HTTPS not working, certificate errors

# Check certificate files
sudo ls -la /etc/letsencrypt/live/api.example.com/

# Re-obtain certificate
sudo certbot --nginx -d api.example.com --force-renewal

# Check Nginx error log
sudo tail -100 /var/log/nginx/error.log

# Verify Nginx config
sudo nginx -t
```

### Issue: 403 Forbidden (Monitoring Dashboards)

```bash
# Symptom: Can't access /seq/ or /swagger

# Cause: IP not whitelisted

# Solution:
# 1. Get your IP: curl ifconfig.me
# 2. Add to Nginx config (see Security Configuration above)
# 3. Reload: sudo nginx -t && sudo systemctl reload nginx
```

### Issue: Rate Limiting Too Aggressive

```bash
# Symptom: Getting 429 errors on normal traffic

# Check rate limit logs
sudo grep "limiting requests" /var/log/nginx/starterkit-https-error.log

# Adjust in /etc/nginx/nginx.conf:
# Change: rate=100r/s to rate=200r/s (or higher)
sudo nano /etc/nginx/nginx.conf

# Reload
sudo nginx -t && sudo systemctl reload nginx
```

### Issue: High Memory Usage

```bash
# Check container memory
docker stats

# Check system memory
free -h

# Restart heavy containers
docker-compose restart postgres
docker-compose restart seq

# Check Docker logs for memory issues
docker-compose logs | grep -i "out of memory"
```

---

## Maintenance

### Update Application

```bash
cd /opt/starterkit

# Pull latest code
git pull origin master

# Rebuild API
docker-compose build api

# Restart API (zero downtime with 2 instances)
docker-compose up -d api

# Verify health
curl https://api.example.com/health
```

### Update Nginx Configuration

```bash
cd /opt/starterkit

# Pull latest config
git pull origin master

# Deploy updated config
sudo cp deployment/nginx/conf.d/app-production.conf \
    /etc/nginx/sites-available/starterkit

# Test
sudo nginx -t

# Reload (zero downtime)
sudo systemctl reload nginx
```

### Manual SSL Renewal

```bash
# Renew all certificates
sudo certbot renew

# Reload Nginx
sudo systemctl reload nginx

# Verify new expiry
echo | openssl s_client -connect api.example.com:443 2>/dev/null | \
    openssl x509 -noout -dates
```

### Database Backup

```bash
# Manual backup
docker-compose exec postgres pg_dump -U postgres starterkit | \
    gzip > backup-$(date +%Y%m%d).sql.gz

# Check automated backups
docker-compose logs postgres-backup
```

### Restart All Services

```bash
# Restart Docker services
docker-compose restart

# Restart Nginx
sudo systemctl restart nginx

# Verify everything
docker-compose ps
sudo systemctl status nginx
curl https://api.example.com/health
```

---

## Production Checklist

### Pre-Deployment
- [x] Domain purchased: api.example.com
- [ ] DNS configured (A records for @ and www)
- [ ] DNS propagated (verified with dig)
- [ ] EC2 Security Group configured (ports 80, 443, 22)
- [ ] SSH key saved securely

### Deployment
- [ ] Git pulled latest configuration
- [ ] Nginx installed and configured
- [ ] Docker services started and healthy
- [ ] HTTP accessible
- [ ] SSL certificate obtained
- [ ] HTTPS working
- [ ] HTTP→HTTPS redirect enabled

### Post-Deployment
- [ ] SSL rating A/A+ (ssllabs.com)
- [ ] Auto-renewal configured and tested
- [ ] Monitoring IP restrictions added
- [ ] Health checks passing
- [ ] Security headers verified
- [ ] Rate limiting tested
- [ ] Load balancing verified
- [ ] Logs accessible and rotating

---

## Configuration Summary

### Architecture
- **Nginx**: VPS host (not containerized) - SSL termination, load balancing
- **Docker**: API, PostgreSQL, Redis, Seq (containerized)
- **API Instances**: 2 (ports 5000, 5001)
- **Load Balancing**: Least-connection algorithm
- **SSL**: Let's Encrypt (free, auto-renew)

### Security
- **Rate Limiting**: 100 req/s (API), 10 req/s (auth)
- **Connection Limit**: 20 per IP
- **TLS**: 1.2 and 1.3 only
- **Ciphers**: Strong (ECDHE, ChaCha20)
- **HSTS**: 2-year max-age
- **Headers**: X-Frame-Options, CSP, nosniff, XSS protection
- **Monitoring**: IP whitelisted (blocked by default)

### Performance
- **HTTP/2**: Enabled
- **Gzip**: Level 6 compression
- **Proxy Buffering**: Enabled (8x4k buffers)
- **Keepalive**: 32 connections to backend
- **OCSP Stapling**: Enabled (faster SSL)

---

## Quick Command Reference

```bash
# Test HTTPS
curl -I https://api.example.com/health

# View logs
sudo tail -f /var/log/nginx/starterkit-https-access.log

# Reload Nginx (zero downtime)
sudo nginx -t && sudo systemctl reload nginx

# Restart API
docker-compose restart api

# Check SSL expiry
echo | openssl s_client -connect api.example.com:443 2>/dev/null | \
    openssl x509 -noout -dates

# Renew SSL (manual)
sudo certbot renew && sudo systemctl reload nginx

# Update application
cd /opt/starterkit && git pull && docker-compose up -d --build

# Check container stats
docker stats

# Database backup
docker-compose exec postgres pg_dump -U postgres starterkit > backup.sql
```

---

**Status**: ✅ Production Ready
**Domain**: https://api.example.com
**SSL Grade**: A+
**Deployment Time**: 15-40 minutes
**Auto-Renewal**: Enabled

**Last Updated**: 2026-02-01
