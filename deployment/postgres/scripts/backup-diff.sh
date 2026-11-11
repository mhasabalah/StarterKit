#!/bin/bash
set -e

echo "Starting differential backup..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit --type=diff backup

echo "Differential backup completed!"
docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit info
