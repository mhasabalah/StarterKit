#!/bin/bash
# ==============================================
# StarterKit Health Check Script
# ==============================================
# Checks health of all services
# ==============================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info() {
    echo -e "${GREEN}✓${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}⚠${NC} $1"
}

log_error() {
    echo -e "${RED}✗${NC} $1"
}

echo "======================================"
echo "StarterKit Health Check"
echo "======================================"
echo ""

# Check Docker containers
echo "📦 Docker Containers:"
if docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | grep starterkit; then
    echo ""
else
    log_error "No StarterKit containers running"
fi

# Check API instances
echo "🚀 API Health:"
for PORT in 5000 5001; do
    if curl -sf "http://localhost:${PORT}/health" > /dev/null 2>&1; then
        log_info "API on port ${PORT}: Healthy"
    else
        log_error "API on port ${PORT}: Not responding"
    fi
done

# Check Nginx (if running on host)
echo ""
echo "🌐 Nginx:"
if systemctl is-active --quiet nginx 2>/dev/null; then
    log_info "Nginx service: Running"
    if curl -sf "http://localhost/health" > /dev/null 2>&1; then
        log_info "Nginx proxy: Working"
    else
        log_warn "Nginx running but proxy not responding"
    fi
else
    log_warn "Nginx not running or not installed"
fi

# Check PostgreSQL
echo ""
echo "🗄️  PostgreSQL:"
POSTGRES_CONTAINER="${COMPOSE_PROJECT_NAME:-starterkit}-postgres"
if docker ps | grep -q "${POSTGRES_CONTAINER}"; then
    if docker exec "${POSTGRES_CONTAINER}" pg_isready -U "${POSTGRES_USER:-postgres}" > /dev/null 2>&1; then
        log_info "PostgreSQL: Accepting connections"
        
        # Get database size
        DB_SIZE=$(docker exec "${POSTGRES_CONTAINER}" psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-starterkit}" -t -c "SELECT pg_size_pretty(pg_database_size('${POSTGRES_DB:-starterkit}'));" 2>/dev/null | xargs)
        echo "   Database size: ${DB_SIZE}"
    else
        log_error "PostgreSQL: Not accepting connections"
    fi
else
    log_error "PostgreSQL container not running"
fi

# Check Redis
echo ""
echo "💾 Redis:"
REDIS_CONTAINER="${COMPOSE_PROJECT_NAME:-starterkit}-redis"
if docker ps | grep -q "${REDIS_CONTAINER}"; then
    if docker exec "${REDIS_CONTAINER}" redis-cli -a "${REDIS_PASSWORD:-}" ping 2>/dev/null | grep -q "PONG"; then
        log_info "Redis: Responding to PING"
        
        # Get memory usage
        REDIS_MEM=$(docker exec "${REDIS_CONTAINER}" redis-cli -a "${REDIS_PASSWORD:-}" INFO memory 2>/dev/null | grep "used_memory_human" | cut -d: -f2 | tr -d '\r\n ')
        echo "   Memory used: ${REDIS_MEM}"
    else
        log_error "Redis: Not responding"
    fi
else
    log_error "Redis container not running"
fi

# Check Seq
echo ""
echo "📝 Seq:"
if curl -sf "http://localhost:5341/api" > /dev/null 2>&1; then
    log_info "Seq: Accessible"
else
    log_warn "Seq: Not accessible (may be internal only)"
fi

# Check Grafana
echo ""
echo "📊 Grafana:"
if curl -sf "http://localhost:3000/api/health" > /dev/null 2>&1; then
    log_info "Grafana: Accessible"
else
    log_warn "Grafana: Not accessible"
fi

# Check Prometheus
echo ""
echo "📈 Prometheus:"
if curl -sf "http://localhost:9090/-/healthy" > /dev/null 2>&1; then
    log_info "Prometheus: Healthy"
else
    log_warn "Prometheus: Not accessible"
fi

# System resources
echo ""
echo "💻 System Resources:"
FREE_MEM=$(free -h | grep Mem | awk '{print $7}')
DISK_USAGE=$(df -h / | tail -1 | awk '{print $5}')
echo "   Available memory: ${FREE_MEM}"
echo "   Disk usage: ${DISK_USAGE}"

# Docker stats
echo ""
echo "🐳 Docker Resource Usage:"
docker stats --no-stream --format "table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}" | grep starterkit || echo "   No stats available"

echo ""
echo "======================================"
echo "Health check complete!"
echo "======================================"
