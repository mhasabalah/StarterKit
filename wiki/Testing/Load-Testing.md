# Load testing (k6)

HTTP load tests that drive the **real API in Docker** with [k6](https://k6.io) (`grafana/k6:1.0.0`, an external tool:
no NuGet package). They guard the hot paths against latency and error-rate regressions. The engine-level import/export
throughput has its own opt-in suite (`datatransfer-perf.yml`, [DataTransfer](../Modules/DataTransfer.md)).

## Run it

```bash
make loadtest            # generate secrets, build + start the stack, seed, run all scenarios, tear down (Docker)
# or step by step
make loadtest-up         # .env.loadtest (random secrets, git-ignored) + postgres, redis, api
make loadtest-seed       # users, an admin, API key, published products - through the public API
make loadtest-run        # the k6 scenarios; exit code 99 when a threshold fails
make loadtest-down       # stop and delete the volumes
```

`tests/load/run.sh` is the single driver (also used by CI). Knobs (environment): `LOAD_DURATION` (30s),
`LOAD_RATE_SCALE` (1; multiplies every arrival rate), `SCENARIOS` (comma list, e.g. `auth,catalog`), `LOAD_USERS`, `LOAD_PRODUCTS`,
`LOAD_IMPORT_ROWS`, `LOADTEST_API_PORT` (5090), `API_CPUS` / `API_MEMORY_LIMIT`. Results land in
`tests/load/results/` (`seed.json`, `summary.json`; git-ignored).

## The stack

`docker-compose.loadtest.yml` is **self-contained** (project `starterkit-load`), not an overlay of
`docker-compose.yml`: the base file needs Docker secrets, pgBackRest, Seq and the migrator, none of which a load run
wants. It runs the API image (`src/Api/Dockerfile`, `ASPNETCORE_ENVIRONMENT=LoadTest`), Postgres 16 (tmpfs, fsync off),
Redis and the k6 container (profile `k6`). Secrets come from `.env.loadtest` (template `.env.loadtest.example`, all
`CHANGE_ME`; `run.sh env` fills random values). The API is published on `127.0.0.1:5090` only. The Hangfire dashboard stays authenticated (user `admin`, password
`HANGFIRE_DASHBOARD_PASSWORD` from the generated env file).

`appsettings.LoadTest.json` is **not part of the published image** (`Api.csproj`: `CopyToPublishDirectory=Never`); the
compose file mounts it read-only into `/app`. The profile therefore exists only where this stack mounts it.

### The LoadTest profile (`src/Api/appsettings.LoadTest.json`)

Only existing options, no backdoor and no test auth handler in the host:

| Setting | Value | Why |
|---|---|---|
| `LoadTest:Enabled` | `true` | Marker the production guard looks for |
| `RateLimiting:Policies[]` | 1,000,000 / minute | The scenarios come from one IP; the limits are tested elsewhere |
| `Identity:TwoFactor:EnforcementMode` | `Warn` | The Admin must call `RequirePermission` endpoints without TOTP; Warn allows them and logs, the supported switch (no role-list tricks) |
| `BotProtection:Enabled` | `false` | Register / login without a CAPTCHA token (already the default) |
| `Database:MigrateOnStartup` | `true` | Single API container, no migrator |
| `Messaging:Transport` | `PostgreSql` | The LoadTest environment is not relaxed: `InMemory` is refused |
| Payment / RabbitMQ secrets | empty | The `CHANGE_ME` guard applies to this environment too (the dashboard password comes from env) |

**`ProductionConfigurationGuard.EnsureLoadTestProfileIsIsolated`** guards both directions: `LoadTest:Enabled=true` in any
environment other than `LoadTest` (Development and Production included) refuses to boot, and so does the `LoadTest`
environment without `LoadTest:Enabled=true` (a mislabelled host running on the base settings). Never point the scripts at
a shared or production deployment.

## Seeding (`tests/load/seed.js`)

Public API only:

1. N users via `POST /api/auth/register` (no email confirmation or CAPTCHA by default); the scenarios sign in as them.
2. One more user, promoted to **Admin** by the seeded SuperAdmin (`Identity:SeedAdmin`, password from
   `LOAD_ADMIN_PASSWORD`). The SuperAdmin first has to change its seeded password (`MUST_CHANGE_PASSWORD`), which the
   script does through `POST /api/auth/change-password`; a re-seed finds it already changed.
3. The Admin gets an **API key** (`X-Api-Key`) scoped `datatransfer.import`, `datatransfer.export`,
   `catalog.products.manage`. Plain users hold no platform permission, so they cannot mint scoped keys (a key never
   exceeds its owner) and use bearer tokens.
4. P products pushed through create, submit, approve, publish, so the public catalog has data.

Output: `seed.json` (emails and ids, no tokens), read by the scenarios. Each VU signs in as its own user on first use
(`lib/session.js`), refreshes through `POST /api/auth/refresh` a minute before the access token expires or after a 401,
and signs in again if the refresh fails, so a run is not bound to the token lifetime. Single-tenant mode (`MultiTenancy:Enabled=false`); set `LOAD_TENANT` to send `X-Tenant` against a
multi-tenant stack.

## Scenarios and thresholds (`tests/load/main.js`)

All run concurrently. `auth`, `catalog`, `products` and `inbox` use the **open model** (`constant-arrival-rate`): k6
starts a fixed number of iterations per second whatever the latency, so a slow server cannot throttle its own load.
An iteration that finds no free VU (pool `maxVUs`) is counted in `dropped_iterations`, which fails the run. `import`
stays one closed-loop VU. Tag: `load_scenario`.

| Scenario | Rate / VUs | Flow | p95 budget |
|---|---|---|---|
| `auth` | 5 it/s (8 to 30 VUs) | `POST /api/auth/login`, then `/api/auth/refresh` (rotated token) | 1500 ms |
| `catalog` | 20 it/s (10 to 40) | anonymous list (page 1), search, product detail | 500 ms |
| `products` | 8 it/s (8 to 30) | create, read, update (with `version`), list mine, delete (bearer) | 800 ms |
| `inbox` | 12 it/s (6 to 30) | `GET /notifications/inbox`, `/unread-count` | 400 ms |
| `import` | 1 VU | multipart `POST /data-transfer/imports` (`catalog.products`, 200 generated rows, Commit), poll `GET /jobs/{id}` until done, API key | 1000 ms (submit and poll requests) |

`LOAD_RATE_SCALE` multiplies the rates. Global: `http_req_failed < 1%`, `checks > 99%`; per scenario
`http_req_failed < 1%`, `dropped_iterations < 1`, and at least one request. The import scenario is exactly one VU because
the API allows one active import per definition and tenant (409 `import_already_running`). The budgets are regression
tripwires with a wide margin, not SLAs.

Each run adds users, products and import rows, so `run.sh run` refuses a second run on the same stack (marker
`tests/load/results/.ran`) unless you pass `--reuse`. Run `make loadtest-down` between runs you want to compare.

## CI

`.github/workflows/load-test.yml` runs nightly (03:30 UTC), on pull requests that change the load-test kit
(`tests/load/**`, the LoadTest profile, `docker-compose.loadtest.yml`, the workflow itself) with the defaults, and on
demand (inputs `duration`, `rate_scale`): validate the
inputs (duration `10s` to `15m`, rate scale 1 to 10; the cap keeps image build, boot, seed, run, the 60s graceful stop and
teardown inside `timeout-minutes: 30`), generate secrets, boot the stack, seed, run k6, upload `summary.json` as the `load-test-summary` artifact, tear down. A crossed
threshold fails the job.

## Baseline

Measured 2026-11-07 with the open model: `make loadtest` equivalent, `LOAD_DURATION=60s`, rate scale 1 (the rates in the
table above), all scenarios at once. Windows 11 laptop (16 logical CPUs, Docker Desktop/WSL2), API container limited to
2 CPUs and 2 GB, Postgres and Redis in containers, k6 on the same host (so not comparable to a hosted runner).

| Scenario | Requests | req/s | avg ms | p95 ms | Failed | Dropped iterations |
|---|---|---|---|---|---|---|
| auth | 600 | 10.0 | 46.2 | 110.4 | 0.00% | 0 |
| catalog | 3582 | 59.7 | 15.8 | 31.6 | 0.00% | 0 |
| products | 2393 | 39.9 | 16.1 | 29.6 | 0.00% | 0 |
| inbox | 1441 | 24.0 | 18.1 | 46.8 | 0.00% | 0 |
| import | 208 | 3.5 | 37.6 | 59.2 | 0.00% | 0 |
| **total** | **8224** | **137.0** | | | **0.00%** (checks 100%) | **0** |

The latencies are far below the budgets because the offered load is modest (about 137 req/s, below saturation of 2 CPUs)
and constant: an earlier closed-model run with 27 VUs looping without pauses pushed about 200 req/s and showed p95 of
160 to 400 ms. Raise `LOAD_RATE_SCALE` to find the knee; the first dropped iterations mark the sustained-rate limit.
Update this table when the first nightly numbers exist (hosted runners are usually 2 to 3 times slower).

## Troubleshooting

- `mfa_required` while seeding: the profile is not loaded (the file is mounted from `src/Api`; the API would refuse
  to boot without it). `MUST_CHANGE_PASSWORD` for the SuperAdmin is handled by the seed script (it changes the seeded
  password on first login); seeing it means `LOAD_ADMIN_PASSWORD` does not match the stack's `Identity:SeedAdmin:Password`
  (or the stack was seeded with another `LOAD_USER_PASSWORD`): `make loadtest-down` and start over.
- 403 `The permission 'catalog.products.manage' is required`: the admin API key lacks a scope (re-seed).
- Git-bash on Windows rewrites `/scripts/...` into a host path; `run.sh` sets `MSYS_NO_PATHCONV=1`.
- Docker Desktop with a small VM: lower `LOAD_RATE_SCALE` and `API_MEMORY_LIMIT`.
