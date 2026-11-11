#!/usr/bin/env bash
# Restore the PostgreSQL cluster from pgBackRest (wiki/deployment/Backups.md). Run from the deployment checkout.
#
#   restore.sh latest                          latest backup + all archived WAL
#   restore.sh time "2026-02-07 14:30:00+00"   point-in-time recovery, then promote
#   restore.sh immediate                       end of the latest backup (consistent, no extra WAL)
#   restore.sh set 20260207-020000F            a specific backup label (see `info`)
#
# Environment: ASSUME_YES=1 skips the confirmation; LOG_LEVEL=detail for pgBackRest's console output;
# RESTART_APP=0 leaves the API/worker stopped (inspect the data first); PGBACKREST_STANZA (default starterkit);
# COMPOSE_ARGS (default "-f docker-compose.yml --env-file .env.prod").
#
# Order: validate the request and the backup BEFORE stopping anything -> stop every writer -> stop PostgreSQL ->
# delta restore (no manual wipe) -> start PostgreSQL -> apply migrations -> start the writers.
set -euo pipefail

STANZA=${PGBACKREST_STANZA:-starterkit}
LOG_LEVEL=${LOG_LEVEL:-info}
read -r -a COMPOSE <<< "${COMPOSE_ARGS:--f docker-compose.yml --env-file .env.prod}"
dc() { docker compose "${COMPOSE[@]}" "$@"; }

usage() {
  sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
}

type=${1:-}
target=${2:-}
case "$type" in
  latest)    target_args=() ;;
  time)      [ -n "$target" ] || usage; target_args=(--type=time "--target=$target" --target-action=promote) ;;
  immediate) target_args=(--type=immediate --target-action=promote) ;;
  set)       [ -n "$target" ] || usage; target_args=("--set=$target") ;;
  *)         usage ;;
esac

echo "==> Checking the repository and the requested backup"
dc exec -T -u postgres pgbackrest pgbackrest --stanza="$STANZA" --log-level-console=warn info
if [ "$type" = set ]; then
  # `info --set=<unknown>` exits 0, so look the label up in the JSON backup list.
  dc exec -T -u postgres pgbackrest pgbackrest --stanza="$STANZA" --output=json info \
    | grep -q "\"label\":\"$target\"" || { echo "Backup set '$target' not found; nothing was stopped." >&2; exit 1; }
fi

if [ "${ASSUME_YES:-0}" != "1" ]; then
  read -r -p "This OVERWRITES the database with '$type${target:+ $target}'. Type 'yes' to continue: " confirm
  [ "$confirm" = yes ] || { echo "Restore cancelled."; exit 1; }
fi

writers=$(dc config --services | grep -xE 'api|worker' | tr '\n' ' ' || true)

echo "==> Stopping writers (${writers:-none}) and PostgreSQL"
[ -n "$writers" ] && dc stop $writers
dc stop postgres

echo "==> Restoring ($type)"
if ! dc run --rm --no-deps -T pgbackrest \
  pgbackrest --stanza="$STANZA" --delta --log-level-console="$LOG_LEVEL" "${target_args[@]}" restore; then
  echo "Restore FAILED. PostgreSQL and the writers are stopped on purpose: the data directory may be partly" >&2
  echo "restored. Fix the cause and run the restore again (delta restore resumes), do not just start PostgreSQL." >&2
  exit 1
fi

echo "==> Starting PostgreSQL"
dc up -d --wait postgres

if [ "${RESTART_APP:-1}" = "1" ]; then
  echo "==> Applying migrations (the production API does not migrate on startup)"
  dc run --rm migrator
  if [ -n "$writers" ]; then
    echo "==> Starting writers"
    dc up -d --no-deps $writers
  fi
else
  echo "==> RESTART_APP=0: API and workers stay stopped. Run the migrator, then start them."
fi

echo "==> Restore finished. Verify the data (module schemas, tenants) before sending traffic."
