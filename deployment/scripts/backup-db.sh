#!/bin/bash
# ==============================================
# StarterKit Database Backup Script
# ==============================================
# Usage: ./scripts/backup-db.sh [backup_name]
# Example: ./scripts/backup-db.sh manual_backup
#
# Creates compressed PostgreSQL backup in /backup directory
# ==============================================

set -e

# Configuration
BACKUP_DIR="${BACKUP_PATH:-/backup}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"
CONTAINER_NAME="${COMPOSE_PROJECT_NAME:-starterkit}-postgres"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
POSTGRES_DB="${POSTGRES_DB:-starterkit}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Functions
log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if container is running
if ! docker ps | grep -q "${CONTAINER_NAME}"; then
    log_error "PostgreSQL container '${CONTAINER_NAME}' is not running"
    exit 1
fi

# Create backup directory
mkdir -p "${BACKUP_DIR}"

# Generate backup filename
BACKUP_NAME="${1:-auto}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="${BACKUP_DIR}/${BACKUP_NAME}_${TIMESTAMP}.sql.gz"

log_info "Starting database backup..."
log_info "Database: ${POSTGRES_DB}"
log_info "Container: ${CONTAINER_NAME}"
log_info "Backup file: ${BACKUP_FILE}"

# Perform backup
if docker exec -t "${CONTAINER_NAME}" pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" --clean --if-exists | gzip > "${BACKUP_FILE}"; then
    BACKUP_SIZE=$(du -h "${BACKUP_FILE}" | cut -f1)
    log_info "Backup completed successfully!"
    log_info "Size: ${BACKUP_SIZE}"
else
    log_error "Backup failed!"
    exit 1
fi

# Cleanup old backups
log_info "Cleaning up backups older than ${RETENTION_DAYS} days..."
find "${BACKUP_DIR}" -name "*.sql.gz" -type f -mtime +${RETENTION_DAYS} -delete

# List recent backups
log_info "Recent backups:"
ls -lh "${BACKUP_DIR}"/*.sql.gz 2>/dev/null | tail -5 || log_warn "No backups found"

log_info "Backup complete!"
