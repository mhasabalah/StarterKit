#!/usr/bin/env bash
# Drives the k6 load-test stack (docker-compose.loadtest.yml). Used by `make loadtest*` and the load-test workflow.
#   tests/load/run.sh env     generate .env.loadtest with random secrets (keeps an existing file)
#   tests/load/run.sh up      build + start postgres, redis and the API (LoadTest profile) and wait until healthy
#   tests/load/run.sh seed    k6 seed.js: users, admin, API keys and published products through the public API
#   tests/load/run.sh run     k6 main.js: all scenarios, thresholds enforced (exit 99 when one fails)
#   tests/load/run.sh down    stop the stack and delete its volumes
#   tests/load/run.sh all     env + up + seed + run, always followed by down
# Env: COMPOSE_PROJECT (starterkit-load), LOADTEST_ENV_FILE (.env.loadtest), LOAD_DURATION, LOAD_VU_SCALE, SCENARIOS.
set -euo pipefail
export MSYS_NO_PATHCONV=1 # git-bash on Windows would rewrite /scripts/... into a host path

cd "$(dirname "$0")/../.."
PROJECT="${COMPOSE_PROJECT:-starterkit-load}"
ENV_FILE="${LOADTEST_ENV_FILE:-.env.loadtest}"
mkdir -p tests/load/results

compose() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.loadtest.yml "$@"; }

secret() { head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 40; }

cmd_env() {
  if [ -f "$ENV_FILE" ]; then echo "$ENV_FILE exists, keeping it"; return; fi
  sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(secret)|" \
      -e "s|^JWT_SECRET=.*|JWT_SECRET=$(secret)|" \
      -e "s|^OTP_HMAC_KEY=.*|OTP_HMAC_KEY=$(secret)|" \
      -e "s|^LOAD_ADMIN_PASSWORD=.*|LOAD_ADMIN_PASSWORD=$(secret)Aa1!|" \
      -e "s|^LOAD_USER_PASSWORD=.*|LOAD_USER_PASSWORD=$(secret)Aa1!|" \
      .env.loadtest.example > "$ENV_FILE"
  echo "wrote $ENV_FILE"
}

cmd_up() { compose up -d --build --wait postgres redis api; }
cmd_seed() { compose run --rm k6 run --quiet /scripts/seed.js; }
cmd_run() { compose run --rm k6 run --quiet /scripts/main.js; }
cmd_down() { compose --profile k6 down -v --remove-orphans; }

case "${1:-all}" in
  env) cmd_env ;;
  up) cmd_up ;;
  seed) cmd_seed ;;
  run) cmd_run ;;
  down) cmd_down ;;
  all)
    cmd_env
    trap 'cmd_down' EXIT
    cmd_up
    cmd_seed
    cmd_run
    ;;
  *) echo "usage: $0 env|up|seed|run|down|all" >&2; exit 2 ;;
esac
