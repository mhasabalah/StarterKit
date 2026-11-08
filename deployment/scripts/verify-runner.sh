#!/usr/bin/env bash
# Checks that this machine can run the deploy, rollback, backup and health workflows (self-hosted runner, `deploy-only`).
# Read-only: it reports what is missing and never installs or changes anything.
#   bash deployment/scripts/verify-runner.sh [deploy-dir]
# Setup guide: wiki/deployment/Self-Hosted-Runner.md
set -uo pipefail

DEPLOY_DIR="${1:-${DEPLOY_DIR:-}}"
MIN_COMPOSE="2.24.0" # docker-compose.worker.yml uses `!reset`
MIN_FREE_GB=5
GUIDE="wiki/deployment/Self-Hosted-Runner.md"
failures=0

ok() { echo "OK    $1"; }

fail() {
    failures=$((failures + 1))
    echo "FAIL  $1"
    # Annotates the run in GitHub Actions; a plain line elsewhere.
    echo "::error::$1 (see $GUIDE)"
}

version_at_least() {
    [ "$(printf '%s\n%s\n' "$2" "$1" | sort -V | head -n1)" = "$2" ]
}

if ! command -v docker >/dev/null 2>&1; then
    fail "Docker CLI is not installed on this runner"
else
    ok "Docker CLI: $(docker --version)"

    if docker info >/dev/null 2>&1; then
        ok "Docker daemon reachable as $(id -un)"
    else
        fail "Cannot reach the Docker daemon as $(id -un): is it running, and is the user in the docker group?"
    fi

    compose_version="$(docker compose version --short 2>/dev/null)"
    compose_version="${compose_version#v}"
    if [ -z "$compose_version" ]; then
        fail "The Docker Compose v2 plugin is not installed"
    elif version_at_least "$compose_version" "$MIN_COMPOSE"; then
        ok "Docker Compose: $compose_version"
    else
        fail "Docker Compose $compose_version is older than the required $MIN_COMPOSE"
    fi
fi

free_gb="$(df -Pk / | awk 'NR==2 { print int($4 / 1048576) }')"
if [ "${free_gb:-0}" -ge "$MIN_FREE_GB" ]; then
    ok "Free disk on /: ${free_gb} GB"
else
    fail "Only ${free_gb:-0} GB free on /, at least ${MIN_FREE_GB} GB is needed for image pulls and backups"
fi

if [ -n "$DEPLOY_DIR" ]; then
    if [ -w "$DEPLOY_DIR" ]; then
        ok "Deploy directory is writable: $DEPLOY_DIR"
    else
        fail "Deploy directory $DEPLOY_DIR is missing or not writable by $(id -un)"
    fi
fi

if [ "$failures" -gt 0 ]; then
    echo "$failures check(s) failed."
    exit 1
fi

echo "Runner is ready."
