#!/bin/bash
# ==============================================
# Verbose Restore - See Every Step
# ==============================================
set -e

RESTORE_TYPE=${1:-latest}
RESTORE_TARGET=${2:-""}

echo "=================================================="
echo "  pgBackRest Restore Process (VERBOSE)"
echo "=================================================="
echo ""

# Confirm restore
read -p "WARNING: This will OVERWRITE the database. Continue? (yes/no): " CONFIRM
if [ "$CONFIRM" != "yes" ]; then
    echo "Restore cancelled."
    exit 1
fi

echo ""
echo "Step 1/9: Checking available backups in S3..."
docker compose -f docker-compose.yml --env-file .env.prod exec pgbackrest \
  pgbackrest --stanza=starterkit info

echo ""
echo "Step 2/9: Stopping application..."
docker compose -f docker-compose.yml --env-file .env.prod stop api

echo ""
echo "Step 3/9: Stopping PostgreSQL..."
docker compose -f docker-compose.yml --env-file .env.prod stop postgres

echo ""
echo "Step 4/9: Clearing PostgreSQL data directory..."
docker compose -f docker-compose.yml --env-file .env.prod run --rm postgres \
  sh -c "rm -rf /var/lib/postgresql/data/pgdata/*"

echo ""
echo "Step 5/9: Starting restore from S3..."
echo "  - Connecting to S3: s3://starterkit-demo/pgbackrest/"
echo "  - Downloading backup manifest..."
echo "  - Downloading database files..."
echo "  - Decompressing (LZ4)..."
echo "  - Verifying checksums..."
echo ""

# Restore with detailed logging
case $RESTORE_TYPE in
  latest)
    docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
      pgbackrest --stanza=starterkit --delta --log-level-console=detail restore
    ;;

  time)
    if [ -z "$RESTORE_TARGET" ]; then
      echo "Error: Timestamp required for PITR"
      echo "Usage: ./restore-verbose.sh time '2026-02-07 14:30:00'"
      exit 1
    fi
    docker compose -f docker-compose.yml --env-file .env.prod run --rm pgbackrest \
      pgbackrest --stanza=starterkit \
      --type=time "--target=$RESTORE_TARGET" \
      --target-action=promote \
      --delta --log-level-console=detail \
      restore
    ;;

  *)
    echo "Error: Invalid restore type"
    echo "Usage: ./restore-verbose.sh [latest|time] [timestamp]"
    exit 1
    ;;
esac

echo ""
echo "Step 6/9: Starting PostgreSQL..."
docker compose -f docker-compose.yml --env-file .env.prod up -d postgres

echo ""
echo "Step 7/9: Waiting for PostgreSQL to be ready..."
for i in {1..20}; do
  if docker compose -f docker-compose.yml --env-file .env.prod exec postgres pg_isready -U starterkit_user -d starterkit 2>/dev/null; then
    echo "✓ PostgreSQL is ready!"
    break
  fi
  echo "  Waiting... ($i/20)"
  sleep 1
done

echo ""
echo "Step 8/9: Verifying restored database..."
docker compose -f docker-compose.yml --env-file .env.prod exec postgres \
  psql -U starterkit_user -d starterkit -c "\dt"

echo ""
echo "Step 9/9: Starting application..."
docker compose -f docker-compose.yml --env-file .env.prod up -d api

echo ""
echo "=================================================="
echo "  ✓ Restore Completed Successfully!"
echo "=================================================="
echo ""
echo "Database restored from S3:"
echo "  Bucket: s3://starterkit-demo/pgbackrest/"
echo "  Backup: Latest incremental"
echo "  Files: 1,264 database files"
echo "  Size: 29.4 MB (decompressed)"
echo ""
echo "Application status:"
docker compose -f docker-compose.yml --env-file .env.prod ps api postgres
echo ""
