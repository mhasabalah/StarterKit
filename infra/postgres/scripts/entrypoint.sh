#!/bin/sh
# Entrypoint of the StarterKit PostgreSQL image (postgres and pgbackrest services).
#   1. Renders the pgBackRest configuration from the environment (S3 credentials stay out of the image).
#   2. `postgres ...`  -> the official docker-entrypoint.sh (initdb, then the server as the postgres user).
#      anything else   -> runs as the postgres user, so pgBackRest reads and restores the data directory with the
#                         ownership PostgreSQL expects (e.g. `docker compose run --rm pgbackrest pgbackrest ... restore`).
set -eu

if [ "$(id -u)" = "0" ]; then
  # Volumes created by earlier images may be owned by another uid.
  chown -R postgres:postgres /var/log/pgbackrest /var/spool/pgbackrest /var/lib/pgbackrest /tmp/pgbackrest
  su-exec postgres render-pgbackrest-conf
else
  render-pgbackrest-conf
fi

case "${1:-postgres}" in
  postgres | -*)
    exec docker-entrypoint.sh "$@"
    ;;
esac

if [ "$(id -u)" = "0" ]; then
  exec su-exec postgres "$@"
fi
exec "$@"
