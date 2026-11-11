#!/bin/bash
# ==============================================
# StarterKit Zero-Downtime Deployment Script
# ==============================================
# This script deploys the StarterKit application with zero downtime
# using Docker Compose rolling updates
# ==============================================

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
PROJECT_DIR="/opt/starterkit"
BACKUP_DIR="$PROJECT_DIR/backups"
LOG_FILE="$PROJECT_DIR/deployment.log"
COMPOSE_FILE="docker-compose.yml"
ENV_FILE="$PROJECT_DIR/.env.prod"

# Functions
log() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')]${NC} $1" | tee -a "$LOG_FILE"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" | tee -a "$LOG_FILE"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1" | tee -a "$LOG_FILE"
}

warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1" | tee -a "$LOG_FILE"
}

# ================================
# Check prerequisites
# ================================
check_prerequisites() {
    log "Checking prerequisites..."
    
    if ! command -v docker &> /dev/null; then
        error "Docker is not installed"
        exit 1
    fi
    
    if ! command -v docker compose &> /dev/null; then
        error "Docker Compose is not installed"
        exit 1
    fi
    
    if [ ! -f "$PROJECT_DIR/$COMPOSE_FILE" ]; then
        error "docker-compose.yml not found in $PROJECT_DIR"
        exit 1
    fi
    
    if [ ! -f "$ENV_FILE" ]; then
        error ".env.prod not found in $PROJECT_DIR"
        exit 1
    fi
    
    log "Using environment file: $ENV_FILE"
    success "Prerequisites check passed"
}

# ================================
# Pre-deployment backup
# ================================
backup() {
    log "Creating pre-deployment backup..."
    
    mkdir -p "$BACKUP_DIR"
    BACKUP_TIMESTAMP=$(date +'%Y%m%d_%H%M%S')
    
    # Backup environment file
    cp "$ENV_FILE" "$BACKUP_DIR/.env.prod.$BACKUP_TIMESTAMP"
    
    # Backup PostgreSQL database
    log "Backing up PostgreSQL database..."
    docker compose -f "$PROJECT_DIR/$COMPOSE_FILE" exec -T postgres \
        pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > "$BACKUP_DIR/postgres_$BACKUP_TIMESTAMP.sql.gz"
    
    # Keep only last 10 backups
    cd "$BACKUP_DIR"
    ls -t postgres_*.sql.gz | tail -n +11 | xargs -r rm
    ls -t .env.* | tail -n +11 | xargs -r rm
    
    success "Backup completed: $BACKUP_TIMESTAMP"
}

# ================================
# Pull new Docker image
# ================================
pull_image() {
    log "Pulling new Docker image..."
    
    cd "$PROJECT_DIR"
    
    # Login to GHCR if token provided
    if [ -n "$GITHUB_TOKEN" ]; then
        echo "$GITHUB_TOKEN" | docker login ghcr.io -u "$GITHUB_ACTOR" --password-stdin
    fi
    
    docker compose -f "$COMPOSE_FILE" pull api migrator
    
    success "Image pulled successfully"
}

# ================================
# Health check
# ================================
health_check() {
    local max_attempts=30
    local attempt=1
    # /health = readiness (Postgres + Redis); /alive = liveness only and would pass while the DB is down.
    local url="http://localhost:5000/health"
    
    log "Performing health check on $url..."
    
    while [ $attempt -le $max_attempts ]; do
        if curl -f -s -o /dev/null "$url"; then
            success "Health check passed (attempt $attempt/$max_attempts)"
            return 0
        fi
        
        log "Health check attempt $attempt/$max_attempts failed, retrying..."
        sleep 2
        attempt=$((attempt + 1))
    done
    
    error "Health check failed after $max_attempts attempts"
    return 1
}

# ================================
# Deploy with zero-downtime
# ================================
deploy() {
    log "Starting deployment..."
    
    cd "$PROJECT_DIR"
    
    # Set image tag
    if [ -z "$IMAGE_TAG" ]; then
        IMAGE_TAG="latest"
        warning "IMAGE_TAG not set, using 'latest'"
    fi
    export IMAGE_TAG
    
    log "Deploying image tag: $IMAGE_TAG"
    
    # Apply migrations first (one-shot migrator, advisory-locked). Abort before touching the API if it fails.
    log "Running database migrator..."
    if ! docker compose -f "$COMPOSE_FILE" run --rm migrator; then
        error "Migration failed - the running API was not touched"
        return 1
    fi

    # Rolling update. The migrator already ran above (exactly once per deploy), so start every OTHER service with
    # --no-deps; a plain `up -d` would start the migrator a second time through depends_on.
    SERVICES=$(docker compose -f "$COMPOSE_FILE" config --services | grep -vx 'migrator' | tr '\n' ' ')
    # The postgres/pgbackrest image (infra/postgres) is built on the host, not pulled: build it (cached) so a
    # changed Dockerfile or pgBackRest template is picked up and `up --no-build` finds the image.
    docker compose -f "$COMPOSE_FILE" build postgres
    # shellcheck disable=SC2086
    docker compose -f "$COMPOSE_FILE" up -d --no-build --no-deps --remove-orphans $SERVICES
    
    # Wait for containers to stabilize
    log "Waiting for containers to stabilize..."
    sleep 10
    
    # Verify running containers
    if ! docker compose -f "$COMPOSE_FILE" ps | grep -q "running"; then
        error "Containers failed to start"
        return 1
    fi
    
    # Health check
    if ! health_check; then
        error "Deployment failed health check"
        return 1
    fi
    
    success "Deployment completed successfully"
}

# ================================
# Cleanup old images
# ================================
cleanup() {
    log "Cleaning up old Docker images..."
    
    docker image prune -f
    
    docker images "ghcr.io/$GITHUB_REPOSITORY/starterkit-api" --format "{{.ID}} {{.CreatedAt}}" | \
        sort -rk 2 | tail -n +4 | awk '{print $1}' | xargs -r docker rmi -f 2>/dev/null || true
    
    success "Cleanup completed"
}

# ================================
# Rollback on failure
# ================================
rollback() {
    error "Deployment failed! Initiating rollback..."
    
    cd "$PROJECT_DIR"
    
    if [ -f "$PROJECT_DIR/deployment/rollback.sh" ]; then
        bash "$PROJECT_DIR/deployment/rollback.sh"
    else
        warning "Rollback script not found, manual intervention required"
    fi
}

# ================================
# Main execution
# ================================
main() {
    log "=========================================="
    log "StarterKit Deployment Started"
    log "=========================================="
    
    check_prerequisites || exit 1
    
    # Load .env.prod
    log "Loading environment variables from .env.prod"
    source "$ENV_FILE"
    
    backup || { error "Backup failed, aborting deployment"; exit 1; }
    
    pull_image || { error "Failed to pull image"; exit 1; }
    
    deploy || { rollback; exit 1; }
    
    cleanup
    
    log "=========================================="
    success "StarterKit Deployment Completed Successfully"
    log "=========================================="
    
    docker compose -f "$PROJECT_DIR/$COMPOSE_FILE" ps
}

# Run main function
main "$@"
