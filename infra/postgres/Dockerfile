# PostgreSQL with pgBackRest, used by both the `postgres` service and the `pgbackrest` backup service
# (docker-compose.yml). PostgreSQL's archive_command runs inside this container, so pgBackRest must be installed
# here; the backup service runs the same image so both always run the same pgBackRest version and the same
# rendered configuration, as the `postgres` user that owns the data directory.
#
# It also carries the pgvector extension, which the Agentic module's knowledge search needs (ADR-0028). pgvector is
# built from source because the Alpine package targets Alpine's own PostgreSQL, not this image's. OPTFLAGS="" keeps
# the build portable across CPUs (no -march=native); with_llvm=no skips the JIT bitcode, which needs the exact clang
# the base image was built with.
FROM postgres:16-alpine

ARG PGBACKREST_VERSION=2.57.0
ARG PGVECTOR_VERSION=0.8.7
# Commit of the tag above: the build refuses a tag that was moved.
ARG PGVECTOR_COMMIT=f37c13f68b57d2c3472b2214fbcff699d6d34876

RUN apk add --no-cache --virtual .pgvector-build build-base git \
    && git clone --depth 1 --branch "v${PGVECTOR_VERSION}" https://github.com/pgvector/pgvector.git /tmp/pgvector \
    && test "$(git -C /tmp/pgvector rev-parse HEAD)" = "${PGVECTOR_COMMIT}" \
    && make -C /tmp/pgvector OPTFLAGS="" with_llvm=no \
    && make -C /tmp/pgvector install with_llvm=no \
    && rm -rf /tmp/pgvector \
    && apk del .pgvector-build

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
