#!/bin/bash
set -e

echo "Starting full backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit --type=full --no-archive-check backup

echo "Full backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit info
