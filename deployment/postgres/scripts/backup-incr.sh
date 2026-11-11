#!/bin/bash
set -e

echo "Starting incremental backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --type=incr backup

echo "Incremental backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit info
