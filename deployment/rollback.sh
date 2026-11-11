#!/bin/bash
# ==============================================
# StarterKit Rollback Script: run a previous API image
# ==============================================
#   deployment/rollback.sh                 list the local API image tags, then ask
#   deployment/rollback.sh <tag>           roll the API (and worker) back to <tag>
#
# DEPLOY_DIR (default /opt/starterkit) must hold docker-compose.yml and .env.prod.
# Only the application image changes: the database keeps its current schema. EF migrations only move forward, so
# an older image runs against the newer schema; if that is not compatible, restore the pre-deploy database too
# (the "Rollback Production Deployment" workflow, restore_database=true, or wiki/deployment/Backups.md).
# The rollback lasts until the next deploy or `docker compose up` with the tag from .env.prod (normally latest).
# ==============================================

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

DEPLOY_DIR="${DEPLOY_DIR:-/opt/starterkit}"
ENV_FILE=".env.prod"

log() { echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1" >&2; }
success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }

dc() { docker compose -f docker-compose.yml --env-file "$ENV_FILE" "$@"; }

# A value from .env.prod without sourcing it (values are not shell-safe).
env_value() { grep -E "^$1=" "$ENV_FILE" | tail -1 | cut -d= -f2- | tr -d '"'"'"; }

list_versions() {
    log "Local images of ${DOCKER_IMAGE}:"
    docker images "$DOCKER_IMAGE" --format "table {{.Tag}}\t{{.CreatedAt}}\t{{.Size}}"
}

rollback_to_version() {
    local version=$1

    if [ -z "$version" ]; then
        error "No version specified"
        list_versions
        exit 1
    fi

    log "Rolling back ${DOCKER_IMAGE} to tag: $version"

    # docker compose reads DOCKER_IMAGE_TAG from the shell before .env.prod.
    export DOCKER_IMAGE_TAG="$version"

    if ! docker image inspect "${DOCKER_IMAGE}:${version}" > /dev/null 2>&1; then
        log "Pulling ${DOCKER_IMAGE}:${version}"
        dc pull api || { error "Image ${DOCKER_IMAGE}:${version} is not available"; exit 1; }
    fi

    local writers
    writers=$(dc config --services | grep -xE 'api|worker' | tr '\n' ' ')
    # shellcheck disable=SC2086
    dc up -d --no-build --no-deps $writers

    local port
    port=$(env_value API_PORT); port=${port:-5000}
    for _ in $(seq 1 30); do
        if curl -fs -o /dev/null "http://localhost:${port}/health"; then
            success "API runs ${DOCKER_IMAGE}:${version}"
            dc ps
            warn "Schema migrations are not rolled back. The next deploy or 'docker compose up' returns to DOCKER_IMAGE_TAG from ${ENV_FILE}."
            return 0
        fi
        sleep 2
    done

    error "Rollback failed health check (http://localhost:${port}/health)"
    dc logs --tail=50 api
    exit 1
}

main() {
    cd "$DEPLOY_DIR"
    [ -f docker-compose.yml ] && [ -f "$ENV_FILE" ] || { error "docker-compose.yml or $ENV_FILE missing in $DEPLOY_DIR"; exit 1; }

    DOCKER_IMAGE=$(env_value DOCKER_IMAGE)
    [ -n "$DOCKER_IMAGE" ] || { error "DOCKER_IMAGE is not set in $ENV_FILE"; exit 1; }

    log "=========================================="
    log "StarterKit Rollback Script"
    log "=========================================="

    if [ $# -eq 0 ]; then
        list_versions
        echo ""
        read -r -p "Enter version tag to roll back to: " version
        rollback_to_version "$version"
    else
        rollback_to_version "$1"
    fi
}

main "$@"
