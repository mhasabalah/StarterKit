#!/bin/bash
set -e

echo "Starting full backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --type=full backup

echo "Full backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit info
