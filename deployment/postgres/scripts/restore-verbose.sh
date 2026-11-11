#!/usr/bin/env bash
# restore.sh with pgBackRest's detailed console output (same arguments: latest | time "<ts>" | immediate | set <label>).
LOG_LEVEL=detail exec "$(dirname "$0")/restore.sh" "$@"
