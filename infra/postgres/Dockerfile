# PostgreSQL with pgBackRest, used by both the `postgres` service and the `pgbackrest` backup service
# (docker-compose.yml). PostgreSQL's archive_command runs inside this container, so pgBackRest must be installed
# here; the backup service runs the same image so both always run the same pgBackRest version and the same
# rendered configuration, as the `postgres` user that owns the data directory.
FROM postgres:16-alpine

ARG PGBACKREST_VERSION=2.57.0

RUN apk add --no-cache "pgbackrest=~${PGBACKREST_VERSION}" su-exec \
    && install -d -o postgres -g postgres -m 0750 \
        /etc/pgbackrest /var/log/pgbackrest /var/spool/pgbackrest /var/lib/pgbackrest /tmp/pgbackrest

COPY pgbackrest.conf.template /etc/pgbackrest/pgbackrest.conf.template
COPY scripts/render-pgbackrest-conf.sh /usr/local/bin/render-pgbackrest-conf
COPY scripts/archive-wal.sh /usr/local/bin/pgbackrest-archive-wal
COPY scripts/entrypoint.sh /usr/local/bin/starterkit-postgres-entrypoint
RUN chmod 0755 /usr/local/bin/render-pgbackrest-conf /usr/local/bin/pgbackrest-archive-wal \
        /usr/local/bin/starterkit-postgres-entrypoint

ENTRYPOINT ["starterkit-postgres-entrypoint"]
CMD ["postgres"]
