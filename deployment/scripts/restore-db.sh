#!/bin/bash
# ==============================================
# StarterKit Database Restore Script
# ==============================================
# Usage: ./scripts/restore-db.sh <backup_file>
# Example: ./scripts/restore-db.sh /backup/manual_20260122_120000.sql.gz
#
# Restores PostgreSQL database from backup
# WARNING: This will overwrite the current database!
# ==============================================

set -e

# Configuration
CONTAINER_NAME="${COMPOSE_PROJECT_NAME:-starterkit}-postgres"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
POSTGRES_DB="${POSTGRES_DB:-starterkit}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check arguments
if [ -z "$1" ]; then
    log_error "Usage: $0 <backup_file>"
    log_error "Example: $0 /backup/manual_20260122_120000.sql.gz"
    exit 1
fi

BACKUP_FILE="$1"

# Check if backup file exists
if [ ! -f "${BACKUP_FILE}" ]; then
    log_error "Backup file not found: ${BACKUP_FILE}"
    exit 1
fi

# Check if container is running
if ! docker ps | grep -q "${CONTAINER_NAME}"; then
    log_error "PostgreSQL container '${CONTAINER_NAME}' is not running"
    exit 1
fi

log_warn "╔════════════════════════════════════════════════════════════╗"
log_warn "║                    ⚠️  WARNING ⚠️                          ║"
log_warn "║                                                            ║"
log_warn "║  This will OVERWRITE the current database!               ║"
log_warn "║  All existing data will be replaced with backup data.     ║"
log_warn "║                                                            ║"
log_warn "╚════════════════════════════════════════════════════════════╝"
echo ""

read -p "Are you sure you want to restore? (type 'yes' to continue): " CONFIRM

if [ "${CONFIRM}" != "yes" ]; then
    log_info "Restore cancelled."
    exit 0
fi

log_info "Starting database restore..."
log_info "Database: ${POSTGRES_DB}"
log_info "Container: ${CONTAINER_NAME}"
log_info "Backup file: ${BACKUP_FILE}"

# Create a backup before restore (just in case)
SAFETY_BACKUP="/tmp/pre_restore_backup_$(date +%Y%m%d_%H%M%S).sql.gz"
log_info "Creating safety backup: ${SAFETY_BACKUP}"
docker exec -t "${CONTAINER_NAME}" pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" --clean --if-exists | gzip > "${SAFETY_BACKUP}"

# Perform restore
log_info "Restoring database..."
if gunzip -c "${BACKUP_FILE}" | docker exec -i "${CONTAINER_NAME}" psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"; then
    log_info "Database restored successfully!"
    log_info "Safety backup kept at: ${SAFETY_BACKUP}"
else
    log_error "Restore failed!"
    log_error "Attempting to restore safety backup..."
    gunzip -c "${SAFETY_BACKUP}" | docker exec -i "${CONTAINER_NAME}" psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"
    exit 1
fi

# Verify restoration
log_info "Verifying database..."
TABLES_COUNT=$(docker exec "${CONTAINER_NAME}" psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';" | xargs)
log_info "Found ${TABLES_COUNT} tables in restored database"

log_info "Restore complete!"
log_info "Remember to restart your API containers: docker-compose restart api api2"
