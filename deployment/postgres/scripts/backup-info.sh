#!/bin/bash
set -e

echo "Fetching backup information..."

docker compose -f docker-compose.yml --env-file .env.prod exec -T -u postgres pgbackrest \
  pgbackrest --stanza=starterkit info
