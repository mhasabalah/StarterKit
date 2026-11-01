#!/bin/bash
set -e

echo "=================================================="
echo "Initializing pgBackRest"
echo "=================================================="

# Create stanza
echo "Creating stanza..."
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --log-level-console=info stanza-create

# Perform initial full backup (skip check, go straight to backup)
echo "Performing initial full backup..."
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit --type=full --no-archive-check --log-level-console=info backup

# Show backup info
echo "Backup information:"
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

echo "=================================================="
echo "pgBackRest initialized successfully!"
echo "=================================================="
