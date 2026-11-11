#!/bin/sh
# Renders /etc/pgbackrest/pgbackrest.conf from the template and the environment:
#   S3_BACKUP_BUCKET, AWS_DEFAULT_REGION, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, POSTGRES_USER, POSTGRES_DB
# The result holds the S3 credentials, so it is written 0640 and never printed.
set -eu

template=${1:-/etc/pgbackrest/pgbackrest.conf.template}
target=${2:-/etc/pgbackrest/pgbackrest.conf}

if [ ! -f "$template" ]; then
  echo "render-pgbackrest-conf: template $template not found" >&2
  exit 1
fi

# Escape a value for the right-hand side of a sed s|..|..| expression.
escape() { printf '%s' "$1" | sed -e 's/[\\|&]/\\&/g'; }

umask 027
tmp="${target}.tmp"
sed -e "s|{{S3_BACKUP_BUCKET}}|$(escape "${S3_BACKUP_BUCKET:-}")|g" \
    -e "s|{{AWS_DEFAULT_REGION}}|$(escape "${AWS_DEFAULT_REGION:-us-east-1}")|g" \
    -e "s|{{AWS_ACCESS_KEY_ID}}|$(escape "${AWS_ACCESS_KEY_ID:-}")|g" \
    -e "s|{{AWS_SECRET_ACCESS_KEY}}|$(escape "${AWS_SECRET_ACCESS_KEY:-}")|g" \
    -e "s|{{POSTGRES_USER}}|$(escape "${POSTGRES_USER:-postgres}")|g" \
    -e "s|{{POSTGRES_DB}}|$(escape "${POSTGRES_DB:-postgres}")|g" \
    "$template" | tr -d '\r' > "$tmp"
chmod 0640 "$tmp"
mv -f "$tmp" "$target"
