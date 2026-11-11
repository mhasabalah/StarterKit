#!/bin/bash
set -e

echo "=================================================="
echo "Initializing pgBackRest"
echo "=================================================="

# Create stanza
echo "Creating stanza..."
docker compose -f docker-compose.yml --env-file .env.prod exec -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --log-level-console=info stanza-create

# Verify that archive_command delivers WAL into the repository (fails if archiving is broken)
echo "Checking WAL archiving..."
docker compose -f docker-compose.yml --env-file .env.prod exec -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --log-level-console=info check

# Perform initial full backup (the archive check is on: the backup is only valid with its WAL)
echo "Performing initial full backup..."
docker compose -f docker-compose.yml --env-file .env.prod exec -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --type=full --log-level-console=info backup

# Show backup info
echo "Backup information:"
docker compose -f docker-compose.yml --env-file .env.prod exec -u postgres pgbackrest \
  pgbackrest --stanza=starterkit info

echo "=================================================="
echo "pgBackRest initialized successfully!"
echo "=================================================="
