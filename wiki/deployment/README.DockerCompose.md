# Docker Compose Configuration Guide

**Project**: StarterKit  
**Date**: 2026-01-21  
**Purpose**: Complete reference for all Docker Compose files and combinations

---

## 📋 Table of Contents

1. [Available Docker Compose Files](#available-docker-compose-files)
2. [Understanding Profiles](#understanding-profiles)
3. [Common Scenarios](#common-scenarios)
4. [Command Reference](#command-reference)
5. [Environment Variables](#environment-variables)
6. [Troubleshooting](#troubleshooting)

---

## 📁 Available Docker Compose Files

### 1. `docker-compose.yml` (Base Configuration)

**Purpose**: Main configuration file with core services

**Services**:
- `postgres` - PostgreSQL database
- `pgadmin` - PostgreSQL admin UI (profile: `tools`)
- `redis` - Redis cache
- `seq` - Structured logging
- `api` - StarterKit API application
- `nginx` - Reverse proxy / Load balancer

**Profiles Used**:
- Default (no profile): postgres, redis, seq, api
- `tools`: pgadmin

**Usage**:
```powershell
# Start basic stack (postgres, redis, seq, api)
docker-compose up -d

# Include pgadmin
docker-compose --profile tools up -d
```

---

### 2. `docker-compose.override.yml` (Development Overrides)

**Purpose**: Development-specific overrides (automatically loaded with docker-compose.yml)

**Services Modified**:
- `api` - Volume mounts for hot reload
- `postgres` - Development configuration
- `redis` - Development settings
- `seq` - Development settings

**Auto-loaded**: This file is automatically used when you run `docker-compose up`

**Usage**:
```powershell
# Automatically applies overrides
docker-compose up -d

# Skip overrides
docker-compose -f docker-compose.yml up -d
```

---

### 3. `docker-compose.prod.yml` (Production Configuration)

**Purpose**: Production-specific configuration

**Services Modified**:
- `api` - Production environment, replicas, health checks
- `postgres` - Production settings, persistent volumes
- `redis` - Production settings, persistence enabled
- `seq` - Production logging
- `pgadmin` - Admin interface (profile: `prod`)

**Profiles Used**:
- `prod`: Production-ready configurations

**Usage**:
```powershell
# Production stack
docker-compose -f docker-compose.yml -f docker-compose.prod.yml --profile prod up -d
```

---

### 4. `docker-compose.observability.yml` (Observability Stack)

**Purpose**: Complete observability and monitoring solution

**Services**:
- `otel-collector` - OpenTelemetry Collector (hub)
- `prometheus` - Metrics storage
- `grafana` - Visualization dashboards
- `tempo` - Distributed tracing backend
- `loki` - Log aggregation
- `jaeger` - Tracing UI
- `zipkin` - Alternative tracing UI
- `node-exporter` - System metrics (profile: `monitoring`)
- `cadvisor` - Container metrics (profile: `monitoring`)

**Profiles Used**:
- `observability`: Core observability (otel, prometheus, grafana, tempo, loki, jaeger, zipkin)
- `prod`: Production-ready observability
- `monitoring`: Additional monitoring tools (node-exporter, cadvisor)

**Usage**:
```powershell
# Core observability stack
docker-compose -f docker-compose.observability.yml --profile observability up -d

# With monitoring tools
docker-compose -f docker-compose.observability.yml --profile observability --profile monitoring up -d
```

---

### 5. `docker-compose.full-stack.yml` (Complete Stack)

**Purpose**: All-in-one configuration (app + observability)

**Services**:
- All API services (postgres, redis, seq, api)
- All observability services
- Monitoring tools

**Profiles Used**:
- `observability`: Enable observability stack
- `monitoring`: Enable monitoring tools

**Usage**:
```powershell
# Everything in one command
docker-compose -f docker-compose.full-stack.yml --profile observability up -d

# With monitoring
docker-compose -f docker-compose.full-stack.yml --profile observability --profile monitoring up -d
```

---

### 6. `docker-compose.nginx-prod.yml` (Production with Nginx + SSL)

**Purpose**: Production deployment with Nginx reverse proxy and SSL/TLS

**Services**:
- `nginx` - Production Nginx with SSL
- `api` - Backend API
- `certbot` - SSL certificate management (profile: `certbot`)

**Profiles Used**:
- `prod`: Production Nginx
- `certbot`: SSL certificate renewal

**Usage**:
```powershell
# Production with Nginx
docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml --profile prod up -d

# With certbot for SSL
docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml --profile prod --profile certbot up -d
```

---

## 🎯 Understanding Profiles

Docker Compose profiles allow you to selectively start services.

### Available Profiles

| Profile | Purpose | Services |
|---------|---------|----------|
| **default** (none) | Core application | postgres, redis, seq, api |
| **tools** | Development tools | pgadmin |
| **prod** | Production setup | Production configs for all services |
| **observability** | Monitoring stack | otel, prometheus, grafana, jaeger, tempo, loki |
| **monitoring** | System monitoring | node-exporter, cadvisor |
| **certbot** | SSL management | certbot |

### How Profiles Work

```powershell
# Without profile: Only default services start
docker-compose up -d

# With profile: Includes profile services
docker-compose --profile observability up -d

# Multiple profiles: Includes all specified
docker-compose --profile observability --profile monitoring up -d
```

---

## 🚀 Common Scenarios

### Scenario 1: Local Development (Minimal)

**What You Need**: API + Database + Cache + Logging

```powershell
# Start core services
docker-compose up -d

# Or explicitly
docker-compose -f docker-compose.yml up -d postgres redis seq api
```

**Access**:
- API: http://localhost:5000
- Seq: http://localhost:5341

---

### Scenario 2: Local Development with Tools

**What You Need**: Development + Database Admin

```powershell
# Include pgAdmin
docker-compose --profile tools up -d
```

**Access**:
- API: http://localhost:5000
- PgAdmin: http://localhost:5050 (admin@example.com / admin)
- Seq: http://localhost:5341

---

### Scenario 3: Development with Full Observability ✅ RECOMMENDED

**What You Need**: API + Observability Stack

```powershell
cd "E:\real\test pro\StarterKit"

# Start everything
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.observability.yml `
  --profile observability `
  up -d
```

**Access**:
- API: http://localhost:5000
- Grafana: http://localhost:3000 (admin/admin)
- Jaeger: http://localhost:16686
- Prometheus: http://localhost:9090
- Seq: http://localhost:5341

**Health Check**:
```powershell
.\test-observability.ps1
```

---

### Scenario 4: Full Stack with Monitoring

**What You Need**: Everything including system metrics

```powershell
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.observability.yml `
  --profile observability `
  --profile monitoring `
  up -d
```

**Additional Access**:
- cAdvisor: http://localhost:8080
- Node Exporter: http://localhost:9100/metrics

---

### Scenario 5: Production Deployment

**What You Need**: Production-ready configuration

```powershell
# Production without observability
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.prod.yml `
  --profile prod `
  up -d

# Production WITH observability
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.prod.yml `
  -f docker-compose.observability.yml `
  --profile prod `
  --profile observability `
  up -d
```

---

### Scenario 6: Production with Nginx + SSL

**What You Need**: Production deployment behind Nginx with SSL

```powershell
# Step 1: Start with Nginx
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.nginx-prod.yml `
  --profile prod `
  up -d

# Step 2: Obtain SSL certificate (first time only)
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.nginx-prod.yml `
  --profile certbot `
  run --rm certbot certonly --webroot -w /var/www/certbot -d yourdomain.com

# Step 3: Restart Nginx
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.nginx-prod.yml `
  restart nginx
```

**Access**:
- HTTPS: https://yourdomain.com
- HTTP redirects to HTTPS automatically

---

### Scenario 7: Quick All-in-One (Using full-stack.yml)

**What You Need**: Everything in one command

```powershell
docker-compose `
  -f docker-compose.full-stack.yml `
  --profile observability `
  --profile monitoring `
  up -d
```

---

## 📝 Command Reference

### Basic Commands

```powershell
# Start services
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f

# View logs for specific service
docker-compose logs -f api

# Restart a service
docker-compose restart api

# Rebuild and start
docker-compose up -d --build

# Remove everything (including volumes)
docker-compose down -v
```

---

### Multi-File Commands

```powershell
# Combine multiple files
docker-compose -f file1.yml -f file2.yml up -d

# Check configuration (dry run)
docker-compose -f file1.yml -f file2.yml config

# List services
docker-compose -f file1.yml -f file2.yml config --services

# View running containers
docker-compose -f file1.yml -f file2.yml ps
```

---

### Profile Commands

```powershell
# Single profile
docker-compose --profile observability up -d

# Multiple profiles
docker-compose --profile observability --profile monitoring up -d

# With multiple files and profiles
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.observability.yml `
  --profile observability `
  up -d
```

---

### Troubleshooting Commands

```powershell
# Check what would run (without starting)
docker-compose config

# Validate configuration
docker-compose config --quiet

# Show service dependencies
docker-compose config --services

# Check container status
docker-compose ps

# View resource usage
docker stats

# Inspect a container
docker inspect <container-name>

# Execute command in container
docker exec -it <container-name> bash
docker exec -it starterkit-api sh
```

---

## 🌍 Environment Variables

### Required Variables

Create `.env` file in project root:

```bash
# Project
COMPOSE_PROJECT_NAME=starterkit

# Database
POSTGRES_DB=starterkit
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres
POSTGRES_PORT=5432

# Redis
REDIS_PASSWORD=redis_password

# Environment
ASPNETCORE_ENVIRONMENT=Development
TZ=UTC

# Observability
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=admin
```

### Example `.env` file

See `.env.example` in the project root.

---

## 🔧 Quick Reference Table

| Scenario | Command |
|----------|---------|
| **Minimal Dev** | `docker-compose up -d` |
| **Dev + Admin Tools** | `docker-compose --profile tools up -d` |
| **Dev + Observability** | `docker-compose -f docker-compose.yml -f docker-compose.observability.yml --profile observability up -d` |
| **Full Stack** | `docker-compose -f docker-compose.full-stack.yml --profile observability --profile monitoring up -d` |
| **Production** | `docker-compose -f docker-compose.yml -f docker-compose.prod.yml --profile prod up -d` |
| **Prod + Observability** | `docker-compose -f docker-compose.yml -f docker-compose.prod.yml -f docker-compose.observability.yml --profile prod --profile observability up -d` |
| **Prod + Nginx + SSL** | `docker-compose -f docker-compose.yml -f docker-compose.nginx-prod.yml --profile prod up -d` |
| **Stop Everything** | `docker-compose -f docker-compose.yml -f docker-compose.observability.yml down` |

---

## 🐛 Troubleshooting

### Issue: "service 'X' is defined multiple times"

**Solution**: Don't combine conflicting files. Use one of these patterns:
- Base only: `docker-compose.yml`
- Base + Observability: `docker-compose.yml` + `docker-compose.observability.yml`
- Base + Prod: `docker-compose.yml` + `docker-compose.prod.yml`
- All-in-one: `docker-compose.full-stack.yml`

❌ Don't do: `docker-compose.yml` + `docker-compose.full-stack.yml` (conflicts)

---

### Issue: Containers not starting

**Diagnostics**:
```powershell
# Check logs
docker-compose logs

# Check specific service
docker-compose logs api

# Check container status
docker-compose ps

# Inspect container
docker inspect <container-name>
```

**Common fixes**:
```powershell
# Rebuild images
docker-compose build --no-cache

# Remove volumes and restart
docker-compose down -v
docker-compose up -d

# Check .env file exists
Get-Content .env
```

---

### Issue: Port conflicts

**Symptom**: "port is already allocated"

**Solution**:
```powershell
# Find what's using the port
netstat -ano | findstr :5000

# Stop the process or change port in docker-compose.yml
```

---

### Issue: Profile services not starting

**Check**:
```powershell
# Verify profile is specified
docker-compose --profile observability config --services

# Should show profile services
```

**Fix**:
```powershell
# Make sure to include --profile flag
docker-compose --profile observability up -d
```

---

## 📚 Additional Resources

- [README.Docker.md](README.Docker.md) - Detailed Docker documentation
- [README.Observability.md](README.Observability.md) - Observability setup guide
- [README.Nginx.md](README.Nginx.md) - Nginx configuration guide
- [TESTING-PRODUCTION.md](TESTING-PRODUCTION.md) - Production testing guide

---

## 🎯 Recommended Workflows

### For Development

```powershell
# 1. Start with observability (most useful)
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.observability.yml `
  --profile observability `
  up -d

# 2. Run health check
.\test-observability.ps1

# 3. View logs
docker-compose logs -f api

# 4. When done
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.observability.yml `
  down
```

### For Testing

```powershell
# 1. Start production-like environment
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.prod.yml `
  -f docker-compose.observability.yml `
  --profile prod `
  --profile observability `
  up -d

# 2. Run tests
.\test-production-ssl.ps1

# 3. Check observability
.\test-observability.ps1
```

### For Production

```powershell
# 1. Set environment variables
$env:ASPNETCORE_ENVIRONMENT = "Production"

# 2. Start production stack
docker-compose `
  -f docker-compose.yml `
  -f docker-compose.prod.yml `
  -f docker-compose.nginx-prod.yml `
  -f docker-compose.observability.yml `
  --profile prod `
  --profile observability `
  up -d

# 3. Monitor
docker-compose logs -f
```

---

## ✅ Summary

| File | Purpose | Use When |
|------|---------|----------|
| `docker-compose.yml` | Base config | Always (required) |
| `docker-compose.override.yml` | Dev overrides | Development (auto-loaded) |
| `docker-compose.prod.yml` | Production config | Deploying to production |
| `docker-compose.observability.yml` | Monitoring stack | Need telemetry/debugging |
| `docker-compose.full-stack.yml` | All-in-one | Quick start everything |
| `docker-compose.nginx-prod.yml` | Nginx + SSL | Production with reverse proxy |

---

**Last Updated**: 2026-01-21  
**Status**: ✅ Complete and tested
