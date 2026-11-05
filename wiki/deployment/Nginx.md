# ==============================================
# StarterKit Nginx Integration Guide
# ==============================================

## 📋 Table of Contents
- [Quick Start](#-quick-start-5-minutes)
- [Overview](#overview)
- [Architecture](#architecture)
- [Configuration Files](#configuration-files)
- [Usage](#usage)
- [Security](#security)
- [SSL Setup](#ssl-setup-lets-encrypt)
- [Performance Tuning](#performance-tuning)
- [Monitoring](#monitoring)
- [Troubleshooting](#troubleshooting)
- [Command Reference](#-command-reference)

---

## 🚀 Quick Start (5 Minutes)

### Development Setup
```bash
# Option A: Using Makefile (recommended)
make nginx-dev

# Option B: Using Docker Compose
docker-compose --profile nginx up -d

# Verify
curl http://localhost/health
curl http://localhost/swagger
```

**Access Points:**
- API via Nginx: `http://localhost`
- Swagger UI: `http://localhost/swagger`
- Health Check: `http://localhost/health`
- Direct API: `http://localhost:8080` (bypasses Nginx)

### Production Setup (Host-Installed Nginx)
```bash
# 1. Copy nginx configurations
sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf
sudo cp deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit

# 2. Enable site
sudo ln -s /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/
sudo rm /etc/nginx/sites-enabled/default

# 3. Test and start
sudo nginx -t
sudo systemctl restart nginx
sudo systemctl enable nginx

# 4. Get SSL certificate (Let's Encrypt) - Optional
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d api.example.com
```

### Common Commands (Production Host)
```bash
# Test configuration
sudo nginx -t

# Reload config (zero downtime)
sudo systemctl reload nginx

# Restart nginx
sudo systemctl restart nginx

# Check status
sudo systemctl status nginx

# View logs
sudo tail -f /var/log/nginx/starterkit-access.log
sudo tail -f /var/log/nginx/starterkit-error.log
```

### Common Commands (Development Docker)
```bash
# View logs
docker logs -f starterkit-nginx

# Test configuration
docker exec starterkit-nginx nginx -t

# Reload config (zero downtime)
docker exec starterkit-nginx nginx -s reload

# Restart
docker-compose restart nginx
```

---

## 🎯 Overview

This Nginx setup provides enterprise-grade reverse proxy capabilities for the StarterKit API with:

✅ **Security**: SSL/TLS, security headers, rate limiting, DDoS protection  
✅ **Performance**: Caching, compression, connection pooling, load balancing  
✅ **Reliability**: Health checks, failover, upstream monitoring  
✅ **Observability**: Structured logging (JSON), metrics, access logs  
✅ **Flexibility**: Environment-specific configs (dev vs prod)

---

## 🏗️ Architecture

```
Client Request
      ↓
  [Nginx :80/443]
      ↓
  Rate Limiting
  Security Headers
  SSL Termination
      ↓
  Load Balancer (upstream)
      ↓
  [API :80] (internal)
```

### Key Features

#### 1. **Reverse Proxy**
- Routes traffic to backend API
- Hides internal architecture
- Single entry point for clients

#### 2. **Load Balancing**
```nginx
upstream starterkit_api {
    least_conn;  # Least connections algorithm
    server api:80 max_fails=3 fail_timeout=30s;
    # Add more instances:
    # server api2:80;
    # server api3:80;
}
```

#### 3. **Security Hardening**
- **SSL/TLS**: Modern protocols (TLSv1.2, TLSv1.3)
- **Security Headers**: HSTS, CSP, X-Frame-Options, etc.
- **Rate Limiting**: Prevents abuse and DDoS
- **Request Filtering**: Blocks malicious patterns

#### 4. **Caching Strategy**
- API responses: Bypass by default (auth-sensitive)
- Static assets: 7-day cache with immutable flag
- Cache zones for different content types

---

## 📁 Configuration Files

StarterKit has **two separate Nginx configurations**:

### 1. Development Configuration (Docker)
**Location**: `infra/nginx/`

Used for local development with Docker Compose. Nginx runs as a container.

```
infra/nginx/
├── Dockerfile              # Custom Nginx image
├── nginx.conf              # Main configuration (Docker)
├── .env.nginx              # Environment variables
├── conf.d/
│   ├── app.conf        # Production config (HTTPS)
│   ├── app-dev.conf    # Development config (HTTP)
│   └── app-test.conf   # Test config
└── ssl/
    ├── cert.pem            # Self-signed SSL certificate
    └── key.pem             # SSL private key
```

### 2. Production Configuration (Host-Installed)
**Location**: `deployment/nginx/`

Used for production deployment on EC2. Nginx is installed on the host (not Docker).

```
deployment/nginx/
├── nginx.conf                          # Main global configuration
│   ├── User and worker settings
│   ├── Event handling configuration
│   ├── HTTP global settings
│   ├── Logging formats (main, json_combined)
│   ├── Gzip compression
│   ├── Rate limiting zones
│   ├── Upstream backend definitions
│   ├── Proxy global settings
│   ├── SSL session cache
│   └── Include directives
└── conf.d/
    └── app-production.conf        # Site-specific configuration
        ├── HTTP server block (port 80)
        ├── Location blocks (API, auth, health, swagger)
        ├── Monitoring routes (Grafana, Seq, Prometheus, Jaeger)
        ├── Security blocks
        └── HTTPS server block (commented, enable when ready)
```

**Key Difference**: 
- **Development** (`infra/nginx/`): Nginx in Docker, routes to `api:80` container
- **Production** (`deployment/nginx/`): Nginx on host, routes to `localhost:5000` and `:5001`

---

### Configuration Structure Benefits

The production configuration follows **nginx best practices** with separated concerns:

✅ **Clean Separation**: Global settings vs site-specific configuration  
✅ **Easy Maintenance**: Changes to one site don't affect global settings  
✅ **Scalable**: Easy to add new sites (staging, dev) in `conf.d/`  
✅ **Consistent**: Same pattern used across all environments  
✅ **Version Control**: Easier to track changes in smaller files

### Configuration Hierarchy (Production)

1. **nginx.conf** (Global Settings)
1. **nginx.conf** (Global Settings)
   - Worker processes and connections
   - Event handling (epoll, multi_accept)
   - HTTP settings (MIME types, compression)
   - **Logging formats**: `main` and `json_combined`
   - **Rate limiting zones**: `api_limit`, `auth_limit`, `general_limit`, `conn_limit`
   - **Upstream definitions**: `starterkit_api` load balancer
   - Proxy global settings (buffering, timeouts, failover)
   - SSL session cache
   - Include directive: loads site configs from `/etc/nginx/sites-enabled/*`

2. **conf.d/app-production.conf** (Site-Specific)
   - HTTP server block (port 80)
   - Server name configuration
   - Site-specific logging paths
   - **Location blocks**:
     - `/` - Main API routes with rate limiting
     - `/api/(auth|login|register|token)` - Stricter rate limits for authentication
     - `/health`, `/alive` - Health checks (no rate limit, no logging)
     - `/swagger` - API documentation
     - `/grafana/`, `/seq/`, `/prometheus/`, `/jaeger/` - Monitoring dashboards
     - `/nginx_status` - Nginx metrics (localhost only)
   - Security blocks (block hidden files, backups, sensitive extensions)
   - HTTPS server block (commented, ready to enable)

### Key Configuration Features

#### Upstream Load Balancing
```nginx
# In nginx.conf
upstream starterkit_api {
    least_conn;  # Route to server with least connections
    
    # Primary API instances (Docker exposed ports)
    server localhost:5000 max_fails=3 fail_timeout=30s weight=1;
    server localhost:5001 max_fails=3 fail_timeout=30s weight=1;
    
    # Optional backup server
    # server localhost:5002 max_fails=3 fail_timeout=30s weight=1 backup;
    
    # Keepalive connections for better performance
    keepalive 32;
    keepalive_timeout 60s;
    keepalive_requests 100;
}
```

**Failover Configuration**:
```nginx
# In nginx.conf - automatic failover on errors
proxy_next_upstream error timeout invalid_header http_500 http_502 http_503;
proxy_next_upstream_tries 2;
proxy_next_upstream_timeout 10s;
```

#### Rate Limiting Strategy
```nginx
# In nginx.conf - Define zones
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/s;
limit_req_zone $binary_remote_addr zone=auth_limit:10m rate=10r/s;
limit_req_zone $binary_remote_addr zone=general_limit:10m rate=50r/s;
limit_conn_zone $binary_remote_addr zone=conn_limit:10m;

# In conf.d/app-production.conf - Apply limits
location / {
    limit_req zone=api_limit burst=50 nodelay;  # Allow bursts
    # ... proxy settings
}

location ~ ^/api/(auth|login|register|token) {
    limit_req zone=auth_limit burst=5 nodelay;  # Strict for auth
    # ... proxy settings
}
```

---

## 🔧 Installation & Setup

### Production Installation (EC2 / Host)

#### Step 1: Install Nginx
```bash
# Update package list
sudo apt update

# Install nginx
sudo apt install nginx -y

# Verify installation
nginx -v
```

#### Step 2: Deploy Configuration
```bash
# Navigate to project directory
cd /opt/starterkit

# Copy main nginx configuration (global settings)
sudo cp deployment/nginx/nginx.conf /etc/nginx/nginx.conf

# Copy site configuration
sudo cp deployment/nginx/conf.d/app-production.conf /etc/nginx/sites-available/starterkit

# Create symlink to enable site
sudo ln -s /etc/nginx/sites-available/starterkit /etc/nginx/sites-enabled/

# Remove default site
sudo rm /etc/nginx/sites-enabled/default
```

#### Step 3: Validate Configuration
```bash
# Test configuration syntax
sudo nginx -t

# Expected output:
# nginx: configuration file /etc/nginx/nginx.conf test is successful
```

#### Step 4: Start Nginx
```bash
# Start nginx service
sudo systemctl start nginx

# Enable on boot
sudo systemctl enable nginx

# Check status
sudo systemctl status nginx

# Verify listening on port 80
sudo netstat -tlnp | grep :80
```

#### Step 5: Test
```bash
# Test health endpoint
curl http://YOUR_SERVER_IP/health

# Test API
curl http://YOUR_SERVER_IP/api/v1/...

# Check logs
sudo tail -f /var/log/nginx/starterkit-access.log
```

---
   - Event loop settings
   - HTTP block defaults
   - Logging format
   - Rate limit zones
   - Upstream definitions
   - Cache paths

2. **app.conf** (Production)
   - HTTPS configuration
   - SSL certificates
   - Security headers
   - Strict rate limiting
   - Production optimizations

3. **app-dev.conf** (Development)
   - HTTP only
   - Relaxed rate limits
   - Debug logging
   - Tool proxies (PgAdmin, Seq, Redis)

---

## 🚀 Usage

### Development Mode

**Start with Nginx:**
```bash
# Using Docker Compose profiles
docker-compose --profile nginx up -d

# Or using Makefile
make nginx-dev
```

**Access Points:**
```
API (via Nginx):      http://localhost
API (direct):         http://localhost:8080
Swagger:              http://localhost/swagger
Health Check:         http://localhost/health
PgAdmin:              http://localhost/pgadmin/
Redis Commander:      http://localhost/redis/
Seq Logs:             http://localhost/logs/
```

### Production Mode

**1. Update .env:**
```bash
NGINX_CONFIG_FILE=app.conf
NGINX_SERVER_NAME=api.example.com
```

**2. Add SSL Certificates:**
```bash
# Place your certificates in:
infra/nginx/ssl/cert.pem
infra/nginx/ssl/key.pem

# Or use Let's Encrypt:
# See "SSL Certificate Management" section
```

**3. Start services:**
```bash
docker-compose --profile prod up -d
```

### Environment-Specific Deployment

```bash
# Development (HTTP only, debug logs)
NGINX_CONFIG_FILE=app-dev.conf docker-compose up -d nginx

# Production (HTTPS, security hardened)
NGINX_CONFIG_FILE=app.conf docker-compose up -d nginx
```

---

## 🔒 Security

### SSL/TLS Configuration

#### Development (Self-Signed Certificate)
The Dockerfile automatically generates a self-signed certificate:
```bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout /etc/nginx/ssl/key.pem \
    -out /etc/nginx/ssl/cert.pem
```

#### Production (Let's Encrypt)

**Option 1: Certbot Manual**
```bash
# Install certbot
apt-get install certbot python3-certbot-nginx

# Get certificate
certbot certonly --webroot \
    -w /var/www/certbot \
    -d api.example.com \
    --email admin@example.com \
    --agree-tos

# Copy certificates
cp /etc/letsencrypt/live/api.example.com/fullchain.pem infra/nginx/ssl/cert.pem
cp /etc/letsencrypt/live/api.example.com/privkey.pem infra/nginx/ssl/key.pem
```

**Option 2: Docker Certbot Integration**
Add to `docker-compose.yml`:
```yaml
certbot:
  image: certbot/certbot
  volumes:
    - ./infra/nginx/ssl:/etc/letsencrypt
    - ./infra/nginx/certbot:/var/www/certbot
  command: certonly --webroot -w /var/www/certbot -d api.example.com --email admin@example.com --agree-tos --no-eff-email
```

### Security Headers

All responses include:
```
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
X-XSS-Protection: 1; mode=block
Content-Security-Policy: default-src 'self'...
Referrer-Policy: strict-origin-when-cross-origin
```

### Rate Limiting

Configured zones:
- **API endpoints**: 100 req/s (burst: 50)
- **Auth endpoints**: 10 req/s (burst: 5)
- **General**: 50 req/s (burst: 20)

Adjust in `.env.nginx`:
```bash
NGINX_API_RATE_LIMIT=100
NGINX_AUTH_RATE_LIMIT=10
```

### Real-time hubs (`/hubs/`)

SignalR (`/hubs/notifications`, [Notification-Realtime-and-Push](../Modules/Notification-Realtime-and-Push.md)) has
its own `location /hubs/` in `deployment/nginx/conf.d/app-production.conf` (and in `infra/nginx/conf.d/*`):

- WebSocket upgrade: `proxy_set_header Upgrade $http_upgrade` and `Connection $connection_upgrade` (a `map` in
  `nginx.conf`: `upgrade` for upgrade requests, empty otherwise so upstream keepalive keeps working);
- `proxy_buffering off`, `proxy_read_timeout 3600s` (well above the 15s hub keep-alive), `proxy_next_upstream off`;
- a per-IP **connection** budget (`limit_conn hub_conn 50`) instead of a request rate: one socket is one request
  that lasts for hours, and it replaces the server-wide `conn_limit 20` for this location;
- **no token in the logs**: the access token travels as `?access_token=`, so the production hub location logs with
  `json_hubs` (path only, `$uri`); the infra configs turn the hub access log off.

With more than one API replica behind `least_conn` there are no sticky sessions: clients must connect with
WebSockets and `skipNegotiation: true` (or add sticky sessions here), and the API uses the Redis backplane.

---

## ⚡ Performance Tuning

### Connection Pooling
```nginx
keepalive 32;              # Persistent connections to backend
keepalive_timeout 65;      # Client keepalive timeout
keepalive_requests 100;    # Requests per connection
```

### Gzip Compression
```nginx
gzip on;
gzip_comp_level 6;
gzip_types text/plain application/json ...;
```

### Buffer Optimization
```nginx
client_body_buffer_size 128k;
client_max_body_size 50M;
proxy_buffers 8 4k;
```

### Caching
```nginx
# API cache (selective)
proxy_cache_path /var/cache/nginx/api;

# Static cache (aggressive)
proxy_cache_path /var/cache/nginx/static inactive=7d;
```

### Load Balancing Strategy

**Least Connections** (default):
```nginx
upstream starterkit_api {
    least_conn;  # Routes to server with fewest connections
    server api:80;
}
```

**Other strategies:**
```nginx
# Round Robin (default if not specified)
upstream starterkit_api {
    server api:80;
    server api2:80;
}

# IP Hash (session persistence)
upstream starterkit_api {
    ip_hash;
    server api:80;
    server api2:80;
}

# Weighted
upstream starterkit_api {
    server api:80 weight=3;
    server api2:80 weight=1;
}
```

---

## 📊 Monitoring

### Logging

#### Access Logs (JSON format)
```json
{
  "time_local": "2024-01-19T01:00:00",
  "remote_addr": "172.18.0.1",
  "request_method": "GET",
  "request_uri": "/api/agents",
  "status": 200,
  "body_bytes_sent": 1024,
  "request_time": 0.123,
  "upstream_response_time": "0.120"
}
```

#### View Logs
```bash
# Real-time access logs
docker logs -f starterkit-nginx

# Access logs (JSON)
docker exec starterkit-nginx tail -f /var/log/nginx/access.log

# Error logs
docker exec starterkit-nginx tail -f /var/log/nginx/error.log

# Parse JSON logs
docker exec starterkit-nginx cat /var/log/nginx/access.log | jq '.'
```

### Health Monitoring
```bash
# Nginx health
curl http://localhost/health

# Upstream health (internal)
docker exec starterkit-nginx nginx -t  # Config test
```

### Metrics Endpoint
```bash
# Expose Nginx metrics (add to app.conf)
location /nginx_status {
    stub_status on;
    access_log off;
    allow 172.16.0.0/12;
    deny all;
}

# Query metrics
curl http://localhost/nginx_status
```

---

## 🔧 Troubleshooting

### Common Issues

#### 1. 502 Bad Gateway
**Cause**: Backend API is down or unreachable

**Fix**:
```bash
# Check API health
docker ps | grep api
curl http://localhost:8080/health

# Check Nginx logs
docker logs starterkit-nginx

# Verify network
docker network inspect starterkit_network
```

#### 2. SSL Certificate Errors
**Cause**: Invalid or expired certificate

**Fix**:
```bash
# Verify certificate
openssl x509 -in infra/nginx/ssl/cert.pem -text -noout

# Check expiration
openssl x509 -in infra/nginx/ssl/cert.pem -enddate -noout

# Regenerate self-signed
cd infra/nginx/ssl
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout key.pem -out cert.pem \
    -subj "/CN=localhost"
```

#### 3. Rate Limit Exceeded (429 Too Many Requests)
**Cause**: Too many requests from single IP

**Fix**:
```bash
# Increase limits in .env.nginx
NGINX_API_RATE_LIMIT=200

# Or disable in development (app-dev.conf)
# Comment out: limit_req zone=...
```

#### 4. Configuration Errors
**Test configuration:**
```bash
# Test config syntax
docker exec starterkit-nginx nginx -t

# Reload without downtime
docker exec starterkit-nginx nginx -s reload

# View parsed config
docker exec starterkit-nginx nginx -T
```

### Debug Mode

Enable debug logging:
```nginx
error_log /var/log/nginx/error.log debug;
```

Restart Nginx:
```bash
docker-compose restart nginx
```

### Performance Issues

**Check upstream response times:**
```bash
# Analyze logs for slow requests
docker exec starterkit-nginx cat /var/log/nginx/access.log | \
    jq 'select(.request_time > 1.0)'
```

**Monitor connections:**
```bash
# Active connections
docker exec starterkit-nginx ps aux | grep nginx
```

---

## 📝 Best Practices

### 1. **Environment Separation**
- Use `app-dev.conf` for development (HTTP, relaxed limits)
- Use `app.conf` for production (HTTPS, security hardened)

### 2. **SSL Management**
- Never commit private keys to Git
- Use Let's Encrypt for production
- Auto-renew certificates

### 3. **Rate Limiting**
- Adjust limits based on traffic patterns
- Monitor 429 responses
- Different limits for different endpoints

### 4. **Caching Strategy**
- Cache static assets aggressively
- Be cautious with API caching (auth issues)
- Use cache invalidation when needed

### 5. **Security**
- Keep Nginx updated
- Review security headers periodically
- Monitor for suspicious activity
- Implement WAF rules if needed

### 6. **Monitoring**
- Integrate with Seq for log aggregation
- Set up alerts for errors
- Monitor upstream health

---

## 🔄 Scaling

### Horizontal Scaling (Multiple API Instances)

**docker-compose.yml:**
```yaml
api:
  deploy:
    replicas: 3  # Run 3 API containers

# Or manually:
services:
  api1:
    image: starterkit-api
    container_name: starterkit-api-1
  
  api2:
    image: starterkit-api
    container_name: starterkit-api-2
  
  api3:
    image: starterkit-api
    container_name: starterkit-api-3
```

**Update nginx.conf:**
```nginx
upstream starterkit_api {
    least_conn;
    server api1:80 max_fails=3 fail_timeout=30s;
    server api2:80 max_fails=3 fail_timeout=30s;
    server api3:80 max_fails=3 fail_timeout=30s;
}
```

### Vertical Scaling (Resource Limits)

```yaml
nginx:
  deploy:
    resources:
      limits:
        cpus: '2.0'
        memory: 1g
```

---

## 📚 References

- [Nginx Official Docs](https://nginx.org/en/docs/)
- [SSL Best Practices](https://wiki.mozilla.org/Security/Server_Side_TLS)
- [Let's Encrypt](https://letsencrypt.org/)
- [Rate Limiting Guide](https://www.nginx.com/blog/rate-limiting-nginx/)

---

## 📝 Command Reference

### Basic Operations
```bash
# Start Nginx with development config
docker-compose --profile nginx up -d

# Start Nginx with production config
NGINX_CONFIG_FILE=app.conf docker-compose up -d nginx

# Stop Nginx
docker-compose stop nginx

# Restart Nginx
docker-compose restart nginx

# View logs (real-time)
docker logs -f starterkit-nginx

# Test configuration
docker exec starterkit-nginx nginx -t

# Reload configuration (zero downtime)
docker exec starterkit-nginx nginx -s reload

# View running config
docker exec starterkit-nginx nginx -T
```

### Testing
```bash
# Test API through Nginx
curl http://localhost/api/health

# Test with headers
curl -H "Authorization: Bearer TOKEN" http://localhost/api/agents

# Test HTTPS
curl -k https://localhost/api/health

# Test rate limiting
for i in {1..200}; do curl http://localhost/api/health; done

# Load test (Apache Bench)
ab -n 1000 -c 10 http://localhost/api/health
```

### SSL Commands
```bash
# Verify certificate
openssl x509 -in infra/nginx/ssl/cert.pem -text -noout

# Check expiration
openssl x509 -in infra/nginx/ssl/cert.pem -enddate -noout

# Test SSL connection
openssl s_client -connect localhost:443 -servername localhost

# Generate self-signed certificate
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout infra/nginx/ssl/key.pem -out infra/nginx/ssl/cert.pem \
    -subj "/CN=localhost"
```

### Monitoring Commands
```bash
# Access logs (JSON format)
docker exec starterkit-nginx tail -f /var/log/nginx/access.log | jq '.'

# Error logs
docker exec starterkit-nginx tail -f /var/log/nginx/error.log

# Parse slow requests (>1 second)
docker exec starterkit-nginx cat /var/log/nginx/access.log | \
    jq 'select(.request_time > 1.0)'

# Connection statistics
docker stats starterkit-nginx

# Active connections
docker exec starterkit-nginx cat /proc/net/sockstat
```

---

**Need help?** Check the [Troubleshooting](#troubleshooting) section or contact the DevOps team.
