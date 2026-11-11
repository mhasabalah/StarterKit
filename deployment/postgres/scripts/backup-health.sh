#!/bin/bash
set -e

echo "Checking backup health..."

# Check backup status
BACKUP_INFO=$(docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --output=json info 2>/dev/null)

if [ -z "$BACKUP_INFO" ]; then
    echo "ERROR: Unable to get backup information"
    exit 1
fi

# Parse last backup time
LAST_BACKUP=$(echo "$BACKUP_INFO" | jq -r '.[0].backup[-1].timestamp.stop // empty')

if [ -z "$LAST_BACKUP" ]; then
    echo "WARNING: No backups found"
    exit 1
fi

# Calculate backup age
CURRENT_TIME=$(date +%s)
BACKUP_TIME=$(date -d "$LAST_BACKUP" +%s 2>/dev/null || date -j -f "%s" "$LAST_BACKUP" +%s 2>/dev/null)
BACKUP_AGE=$((CURRENT_TIME - BACKUP_TIME))
BACKUP_AGE_HOURS=$((BACKUP_AGE / 3600))

echo "Last backup: $LAST_BACKUP ($BACKUP_AGE_HOURS hours ago)"

# Alert if backup is older than 25 hours
if [ $BACKUP_AGE -gt 90000 ]; then
  echo "WARNING: Last backup is $BACKUP_AGE_HOURS hours old (threshold: 25 hours)"
  exit 1
fi

# Verify backup integrity
echo "Verifying backup integrity..."
docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit check

echo "✓ Backup health check passed"
echo "✓ Last backup: $BACKUP_AGE_HOURS hours ago"
exit 0
