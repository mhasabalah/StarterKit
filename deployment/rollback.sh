#!/bin/bash
# ==============================================
# StarterKit Rollback Script
# ==============================================
# Rollback to a previous Docker image version
# ==============================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

PROJECT_DIR="/opt/starterkit"
COMPOSE_FILE="docker-compose.yml"

log() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

# List available versions
list_versions() {
    log "Available Docker image versions:"
    docker images "ghcr.io/$GITHUB_REPOSITORY/starterkit-api" --format "table {{.Tag}}\t{{.CreatedAt}}\t{{.Size}}"
}

# Rollback to specific version
rollback_to_version() {
    local version=$1
    
    if [ -z "$version" ]; then
        error "No version specified"
        list_versions
        exit 1
    fi
    
    log "Rolling back to version: $version"
    
    cd "$PROJECT_DIR"
    
    # Set the image tag
    export IMAGE_TAG="$version"
    
    # Update the deployment
    docker compose -f "$COMPOSE_FILE" up -d --no-build api
    
    # Wait and check health
    sleep 10
    
    if curl -f -s -o /dev/null "http://localhost:5000/health"; then
        success "Rollback to version $version successful"
        docker compose -f "$COMPOSE_FILE" ps
    else
        error "Rollback failed health check"
        exit 1
    fi
}

# Main
main() {
    log "=========================================="
    log "StarterKit Rollback Script"
    log "=========================================="
    
    # Load env
    if [ -f "$PROJECT_DIR/.env" ]; then
        source "$PROJECT_DIR/.env"
    fi
    
    if [ $# -eq 0 ]; then
        list_versions
        echo ""
        read -p "Enter version tag to rollback to: " version
        rollback_to_version "$version"
    else
        rollback_to_version "$1"
    fi
}

main "$@"
