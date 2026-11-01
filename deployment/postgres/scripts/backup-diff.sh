#!/bin/bash
set -e

echo "Starting differential backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit --type=diff --no-archive-check backup

echo "Differential backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit info
