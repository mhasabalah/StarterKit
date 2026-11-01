#!/bin/bash
set -e

echo "Starting incremental backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit --type=incr --no-archive-check backup

echo "Incremental backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T pgbackrest \
  pgbackrest --stanza=starterkit info
