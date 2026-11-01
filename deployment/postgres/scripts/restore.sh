#!/bin/bash
set -e

RESTORE_TYPE=${1:-latest}  # latest, time, immediate
RESTORE_TARGET=${2:-""}    # timestamp for PITR

echo "=================================================="
echo "pgBackRest Restore Process"
echo "=================================================="

# Confirm restore
read -p "WARNING: This will OVERWRITE the database. Continue? (yes/no): " CONFIRM
if [ "$CONFIRM" != "yes" ]; then
    echo "Restore cancelled."
    exit 1
fi

echo "Stopping application..."
docker compose -f docker-compose.yml --env-file .env.prod stop api

echo "Stopping PostgreSQL..."
docker compose -f docker-compose.yml --env-file .env.prod stop postgres

# Clear data directory
echo "Clearing PostgreSQL data directory..."
docker compose -f docker-compose.yml --env-file .env.prod run --rm postgres \
  sh -c "rm -rf /var/lib/postgresql/data/pgdata/*"

# Restore based on type
case $RESTORE_TYPE in
  latest)
    echo "Restoring latest backup..."
    docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
      pgbackrest --stanza=starterkit --delta restore
    ;;

  time)
    if [ -z "$RESTORE_TARGET" ]; then
      echo "Error: Timestamp required for PITR"
      echo "Usage: ./restore.sh time '2026-02-07 14:30:00'"
      exit 1
    fi
    echo "Restoring to point-in-time: $RESTORE_TARGET"
    docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
      pgbackrest --stanza=starterkit \
      --type=time "--target=$RESTORE_TARGET" \
      --target-action=promote \
      restore
    ;;

  immediate)
    echo "Restoring to last consistent state..."
    docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
      pgbackrest --stanza=starterkit \
      --type=immediate \
      --target-action=promote \
      restore
    ;;

  *)
    echo "Error: Invalid restore type"
    echo "Usage: ./restore.sh [latest|time|immediate] [timestamp]"
    echo "Examples:"
    echo "  ./restore.sh latest"
    echo "  ./restore.sh time '2026-02-07 14:30:00'"
    echo "  ./restore.sh immediate"
    exit 1
    ;;
esac

# Start PostgreSQL
echo "Starting PostgreSQL..."
docker compose -f docker-compose.yml --env-file .env.prod up -d postgres

# Wait for PostgreSQL to be ready
echo "Waiting for PostgreSQL to be ready..."
sleep 10

# Verify database
echo "Verifying database..."
docker compose -f docker-compose.yml --env-file .env.prod exec postgres \
  psql -U starterkit_user -d starterkit -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';"

# Start application
echo "Starting application..."
docker compose -f docker-compose.yml --env-file .env.prod up -d api

echo "=================================================="
echo "Restore completed successfully!"
echo "Please verify database integrity."
echo "=================================================="
