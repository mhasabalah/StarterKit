#!/bin/sh
# PostgreSQL archive_command: pgbackrest-archive-wal %p (the name must contain "pgbackrest": stanza-create checks it)
#
# With backups configured (S3_BACKUP_BUCKET set), every WAL segment is pushed with pgBackRest and a failure is a
# real failure: PostgreSQL keeps the segment and retries. pgBackRest's archive-push-queue-max (template) caps how
# much WAL can pile up while the repository is unreachable, so an outage cannot fill the data volume.
#
# Without a configured repository there is nowhere to send WAL, so the segment is reported as archived and
# PostgreSQL recycles it. archive_mode=on with a command that always fails would otherwise keep every segment
# forever and eventually fill the disk.
set -eu

case "${S3_BACKUP_BUCKET:-}" in
  "" | your-backup-bucket-name | *CHANGE_ME*)
    exit 0
    ;;
esac

exec pgbackrest --stanza="${PGBACKREST_STANZA:-starterkit}" archive-push "$1"
