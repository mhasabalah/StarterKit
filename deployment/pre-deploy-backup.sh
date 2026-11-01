#!/bin/bash
# ==============================================
# StarterKit Pre-Deployment Backup Script
# ==============================================
# Create backups before deployment
# ==============================================

set -e

# Colors
BLUE='\033[0;34m'
GREEN='\033[0;32m'
NC='\033[0m'

PROJECT_DIR="/opt/starterkit"
BACKUP_DIR="$PROJECT_DIR/backups"
TIMESTAMP=$(date +'%Y%m%d_%H%M%S')

log() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')]${NC} $1"
}

success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

main() {
    log "Creating pre-deployment backup..."
    
    mkdir -p "$BACKUP_DIR"
    
    # Backup .env
    if [ -f "$PROJECT_DIR/.env" ]; then
        cp "$PROJECT_DIR/.env" "$BACKUP_DIR/.env.$TIMESTAMP"
        log "Environment file backed up"
    fi
    
    # Backup database
    log "Backing up PostgreSQL database..."
    docker compose -f "$PROJECT_DIR/docker-compose.yml" exec -T postgres \
        pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | \
        gzip > "$BACKUP_DIR/postgres_$TIMESTAMP.sql.gz"
    
    success "Backup completed: $TIMESTAMP"
    
    # Cleanup old backups (keep last 10)
    cd "$BACKUP_DIR"
    ls -t postgres_*.sql.gz | tail -n +11 | xargs -r rm
    ls -t .env.* | tail -n +11 | xargs -r rm
    
    log "Old backups cleaned up"
}

main "$@"
