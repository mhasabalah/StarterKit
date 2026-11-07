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
`LOAD_VU_SCALE` (1), `SCENARIOS` (comma list, e.g. `auth,catalog`), `LOAD_USERS`, `LOAD_PRODUCTS`,
`LOAD_IMPORT_ROWS`, `LOADTEST_API_PORT` (5090), `API_CPUS` / `API_MEMORY_LIMIT`. Results land in
`tests/load/results/` (`seed.json`, `summary.json`; git-ignored).

## The stack

`docker-compose.loadtest.yml` is **self-contained** (project `starterkit-load`), not an overlay of
`docker-compose.yml`: the base file needs Docker secrets, pgBackRest, Seq and the migrator, none of which a load run
wants. It runs the API image (`src/Api/Dockerfile`, `ASPNETCORE_ENVIRONMENT=LoadTest`), Postgres 16 (tmpfs, fsync off),
Redis and the k6 container (profile `k6`). Secrets come from `.env.loadtest` (template `.env.loadtest.example`, all
`CHANGE_ME`; `run.sh env` fills random values). The API is published on `localhost:5090`.

### The LoadTest profile (`src/Api/appsettings.LoadTest.json`)

Only existing options, no backdoor and no test auth handler in the host:

| Setting | Value | Why |
|---|---|---|
| `LoadTest:Enabled` | `true` | Marker the production guard looks for |
| `RateLimiting:Policies[]` | 1,000,000 / minute | The scenarios come from one IP; the limits are tested elsewhere |
| `Identity:TwoFactor:RequiredForRoles` | three non-existent role names | The Admin must call `RequirePermission` endpoints without TOTP. All three entries are replaced because arrays merge by index |
| `BotProtection:Enabled` | `false` | Register / login without a CAPTCHA token (already the default) |
| `Database:MigrateOnStartup` | `true` | Single API container, no migrator |
| `Messaging:Transport` | `PostgreSql` | The LoadTest environment is not relaxed: `InMemory` is refused |
| Payment / RabbitMQ / Hangfire dashboard secrets | empty | The `CHANGE_ME` guard applies to this environment too |

**`ProductionConfigurationGuard.EnsureLoadTestProfileIsIsolated`** refuses to boot any environment other than `LoadTest`
(Development and Production included) when `LoadTest:Enabled=true`, so the profile cannot leak into a real deployment.
Never point the scripts at a shared or production deployment.

## Seeding (`tests/load/seed.js`)

Public API only:

1. N users via `POST /api/auth/register` (no email confirmation or CAPTCHA by default); their bearer tokens are kept.
2. One more user, promoted to **Admin** by the seeded SuperAdmin (`Identity:SeedAdmin`, password from
   `LOAD_ADMIN_PASSWORD`). The SuperAdmin first has to change its seeded password (`MUST_CHANGE_PASSWORD`), which the
   script does through `POST /api/auth/change-password`; a re-seed finds it already changed.
3. The Admin gets an **API key** (`X-Api-Key`) scoped `datatransfer.import`, `datatransfer.export`,
   `catalog.products.manage`. Plain users hold no platform permission, so they cannot mint scoped keys (a key never
   exceeds its owner) and use bearer tokens.
4. P products pushed through create, submit, approve, publish, so the public catalog has data.

Output: `seed.json`, read by the scenarios. Bearer tokens last `JwtSettings:AccessTokenExpirationMinutes`; re-seed for
longer sessions. Single-tenant mode (`MultiTenancy:Enabled=false`); set `LOAD_TENANT` to send `X-Tenant` against a
multi-tenant stack.

## Scenarios and thresholds (`tests/load/main.js`)

All run concurrently as `constant-vus` scenarios (tag `load_scenario`).

| Scenario | VUs | Flow | p95 budget |
|---|---|---|---|
| `auth` | 4 | `POST /api/auth/login`, then `/api/auth/refresh` (rotated token) | 1500 ms |
| `catalog` | 10 | anonymous list (page 1), search, product detail | 500 ms |
| `products` | 6 | create, read, update (with `version`), list mine, delete (bearer) | 800 ms |
| `import` | 1 | multipart `POST /data-transfer/imports` (`catalog.products`, 200 generated rows, Commit), poll `GET /jobs/{id}` until done, API key | 1000 ms (submit and poll requests) |
| `inbox` | 6 | `GET /notifications/inbox`, `/unread-count` | 400 ms |

Global: `http_req_failed < 1%`, `checks > 99%`; per scenario `http_req_failed < 1%`, and at least one request. The import
scenario is exactly one VU because the API allows one active import per definition and tenant (409
`import_already_running`). The budgets are regression tripwires with a wide margin, not SLAs: tighten them as the
baseline firms up.

## CI

`.github/workflows/load-test.yml` runs nightly (03:30 UTC) and on demand (inputs `duration`, `vu_scale`): generate
secrets, boot the stack, seed, run k6, upload `summary.json` as the `load-test-summary` artifact, tear down. A crossed
threshold fails the job.

## Baseline

Measured 2026-11-06, `make loadtest` equivalent with `LOAD_DURATION=60s`, 27 VUs, all scenarios at once. Windows 11
laptop (16 logical CPUs, Docker Desktop/WSL2), API container limited to 2 CPUs and 2 GB, Postgres and Redis in
containers, k6 on the same host (so the numbers are conservative for the API and not comparable to a hosted runner).

| Scenario | Requests | req/s | avg ms | p95 ms | Failed |
|---|---|---|---|---|---|
| auth | 1358 | 22.4 | 176.6 | 404.1 | 0.00% |
| catalog | 4464 | 73.7 | 67.7 | 162.0 | 0.00% |
| products | 3725 | 61.5 | 76.7 | 194.6 | 0.00% |
| inbox | 2242 | 37.0 | 60.0 | 184.8 | 0.00% |
| import | 165 | 2.7 | 101.9 | 259.7 | 0.00% |
| **total** | **11954** | **197.5** | | | **0.00%** (checks 100%) |

`auth` is the slowest path by design (password hashing). Hosted GitHub runners are usually 2 to 3 times slower, which the
budgets allow for; update this table when the first nightly numbers exist.

## Troubleshooting

- `MUST_CHANGE_PASSWORD` or `mfa_required` while seeding: the image does not carry the LoadTest profile (stale build);
  `docker compose ... build api`.
- 403 `The permission 'catalog.products.manage' is required`: the admin API key lacks a scope (re-seed).
- Git-bash on Windows rewrites `/scripts/...` into a host path; `run.sh` sets `MSYS_NO_PATHCONV=1`.
- Docker Desktop with a small VM: lower `LOAD_VU_SCALE` and `API_MEMORY_LIMIT`.
