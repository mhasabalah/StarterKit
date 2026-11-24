# Search operations

How to run the Search module in production: the first deployment, sizing the `search` queue, the signals to watch,
the answer to each alert, and routine tasks. What the module does is in [Search module](../Modules/Search.md).

Search never fails a request: when an index cannot answer (not built yet, engine down), the owning module's SQL search
answers instead. That makes problems quiet, so the metrics and alerts below are how you notice them.

## 1. First deployment

1. Apply the migrations (`--migrate-only`, as for every module). They create the `search` schema and the `pg_trgm`
   extension. A hardened migration role without `CREATE` on the database needs the extension created beforehand
   (`deployment/postgres/scripts/init-rls-roles.sql` does it).
2. Start the API. With at least one registered index, `ReconcileSearchIndexesJob` runs once at startup and enqueues a
   rebuild of every index in every tenant on the `search` queue; after that it runs on `Search:Reconcile:Cron`.
3. Watch the indexes become `Ready`: `GET /api/v1/search/indexes` (tenant, `search.manage`) shows status, documents
   and the last error. Until then the owners' SQL search answers, and `search.queries{status="index_not_ready"}`
   counts those searches.

New tenants get their indexes the same way, on the next reconcile run.

## 2. Size the `search` queue

Rebuilds and drift checks run on the Hangfire queue `search` (`Hangfire:Servers:Search`, enabled with 2 workers per
replica). Live changes do not: they are message consumers, indexed seconds after the change commits.

- A rebuild streams the whole index of one tenant in `Search:Indexing:BatchSize` batches. Many tenants after a
  definition change means many rebuilds; more workers finish them sooner, at the cost of database (and engine) load.
- To keep rebuilds off the API replicas, use `docker-compose.worker.yml`: `Hangfire__Servers__Search__Enabled=false`
  on `api` and `true` on `worker`. With Meilisearch, add `docker-compose.meilisearch-worker.yml` so the worker
  reaches the engine too.
- Some replica must run the queue. An instance without it logs a warning at startup; if no instance runs it, indexes
  are never built (`SearchIndexNotReady`).

## 3. Watch the signals

Meter `StarterKit.Search` (exported by OpenTelemetry). Tags never carry query text, tenant ids or document ids.

| Signal | Healthy |
|---|---|
| `search.queries{index,status,match_mode}` | `status="ok"` almost always; `engine_unavailable` and `index_not_ready` close to zero once indexes are built |
| `search.query.duration{index}` | Tens of milliseconds; the tail grows with broad queries and facets |
| `search.index.lag{index}` | Seconds from a committed change to its indexing |
| `search.documents.indexed{index,operation}` | Follows the owners' write volume |
| `search.rebuilds{index,outcome}` | `built` and `uptodate`; `failed` needs a look; `busy` and `leaselost` are harmless races |
| `search.drift.documents{index,kind}` | Zero or close to it (each one is a change the index missed) |
| `search.analytics.terms_dropped` | Zero; otherwise raise `Analytics:MaxPendingTerms` or shorten `Analytics:FlushSeconds` |
| `/health` entry `search-engine` | Meilisearch only: Degraded when the server does not answer (never Unhealthy) |
| `GET /api/v1/search/indexes` | Every index `Ready` and `upToDate`, no `lastErrorCode`, a recent `drift.checkedAt` with few documents out of sync |
| `GET /api/v1/admin/search/engine` | The configured engine and each definition's fingerprint (`search.operate`) |

## 4. Answer an alert

The rules are in `infra/observability/configs/alerts.yaml` (group `search`).

| Alert | Likely cause | Do |
|---|---|---|
| `SearchEngineUnavailable` | PostgreSQL: statements hit `Search:Postgres:StatementTimeoutMs`, or the database is overloaded or unreachable. Meilisearch: the server is down, slow or refuses the key, and the circuit breaker is open | PostgreSQL: look for broad queries in `pg_stat_statements` and the database load; raise the timeout only with a reason. Meilisearch: `/health`, the container log, the key in `secrets/meilisearch_api_key.txt`. Searches are answered by SQL meanwhile |
| `SearchIndexNotReady` | Nothing runs the `search` queue, or every rebuild of the index fails | Hangfire dashboard → Servers: one must list the `search` queue. Then `GET /api/v1/search/indexes/{key}` for `lastErrorCode` |
| `SearchIndexingLagHigh` | Messages wait in the outbox or the bus, or the engine writes slowly | Check `OutboxBacklogGrowing` and `MessageConsumerFaults` (the consumer's error queue), then the engine's write latency. Lost changes are repaired by the drift check |
| `SearchRebuildFailing` | The owner's source throws (`source_failed`), or the engine refuses the documents | Read `lastErrorCode` and the API log of `RebuildSearchIndexJob`. The previous build keeps answering. Reconcile retries a failed rebuild at most once an hour; `POST .../rebuild` retries now |
| `SearchDriftRecurring` | An aggregate changes a searchable value without raising `SearchDocumentsChangedIntegrationEvent`, or search messages were dead-lettered | Auto-repair fixes the documents (`Search:Drift:AutoRepair`). Find the cause: `kind="stale"` means missed updates, `missing` missed creates or restores, `orphaned` missed deletes. Check the outbox dead letters of the owner's context |

## 5. Routine tasks

- **Rebuild one index of a tenant:** `POST /api/v1/search/indexes/{key}/rebuild` (`search.manage`). 409
  `search_rebuild_running` while a rebuild holds the lease. If a worker died mid-rebuild, the previous build keeps
  answering and the lease expires after `Search:Indexing:LeaseMinutes`; rebuild again then.
- **Rebuild an index in every tenant:** `POST /api/v1/admin/search/indexes/{key}/rebuild-all` (`search.operate`,
  SuperAdmin, host only). Use it after an engine switch or an incident.
- **Change a definition** (a field, a weight, an analyzer) or the normalizer: deploy it. The fingerprint changes and
  reconcile rebuilds every tenant on its own, while the old build keeps answering.
- **Check one index now:** `POST /api/v1/search/indexes/{key}/drift-check`; the findings appear on the index.
- **Switch engines:** set `Search:Engine` (and the engine's settings), deploy, then rebuild every index with
  `rebuild-all`. Documents are not moved between engines; until a tenant's index is rebuilt, SQL answers.
  Meilisearch setup: [Meilisearch engine](Meilisearch.md#setup).
<!--#if (sample)-->
- **Take one owner off the index:** owners decide when they use it. Catalog reads `Catalog:Search:UseSearchIndex`
  (`false` keeps every product query in SQL) without touching the index or its sync.
<!--#endif-->
- **Search analytics:** counts per day are kept for `Search:Analytics:RetentionDays`; `Search:Analytics:Enabled=false`
  stops counting.

Never write the `search` tables by hand. Documents are written only by the engine (with the version guard and the
rebuild stamps), the index states hold leases and stamps that rebuilds depend on, and analytics rows only by
`SearchAnalyticsStore`. Rebuild instead.

## Verify

- `GET /api/v1/search/indexes` in a tenant lists every registered index as `Ready`.
- Prometheus `/alerts` lists the `search` group, and `search_queries_total` appears after the first search.
- A changed record is found by its new text within seconds (`search.index.lag`).
<!--#if (sample)-->
- Under load: the `search` scenario ([Load testing](../Testing/Load-Testing.md)) reports `search_from_index`, the
  share of searches the index answered.
<!--#endif-->

## Related

[Search module](../Modules/Search.md), [PostgreSQL engine](Postgres-Engine.md), [Meilisearch engine](Meilisearch.md),
[Observability](../deployment/Observability.md), [Messaging](../Architecture/Messaging.md).
