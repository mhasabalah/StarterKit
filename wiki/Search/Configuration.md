# Search configuration

Every setting of the Search module, section `Search` (validated at startup: the host refuses to start on an invalid
value).

Part of the [Search module](../Modules/Search.md).

## Settings

| Key | Default | Meaning |
|---|---|---|
| `Engine` | `Postgres` | `Postgres` or `Meilisearch` |
| `Query:MaxPageSize` / `MaxWindow` / `MaxTextLength` / `MaxTerms` | 100 / 1000 / 200 / 10 | query limits |
| `Query:Fuzzy:Enabled` / `MinTermLength` / `Threshold` | true / 3 / 0.4 | close-spelling fallback |
| `Facets:MaxValues` / `ScanLimit` | 20 / 10000 | facet counts |
| `Postgres:StatementTimeoutMs` | 2000 | timeout of every search query |
| `Meilisearch:Url` / `ApiKey` / `IndexPrefix` | | required with `Engine=Meilisearch` (the key from a secret) |
| `Meilisearch:TimeoutSeconds` / `TaskTimeoutSeconds` | 10 / 30 | per request, and per write task |
| `Indexing:BatchSize` / `LeaseMinutes` | 500 / 30 | rebuilds |
| `Reconcile:Cron`, `Drift:Cron` / `AutoRepair` / `MaxDocuments` | every 15 min, 03:00 / true / 200000 | index upkeep |
| `Analytics:Enabled` / `RetentionDays` / `MaxQueryLength` / `FlushSeconds` | true / 90 / 64 / 30 | zero-result analytics |
| `Analytics:MaxPendingTerms` / `PurgeCron` | 10000 / 03:30 | analytics buffer bound and retention purge |

## Elsewhere

| Key | Default | Meaning |
|---|---|---|
| `Hangfire:Servers:Search:Enabled` / `WorkerCount` | true / 2 | the Hangfire server of the `search` queue (rebuilds, drift checks) on this instance; some replica must run it ([sizing](Operations.md#2-size-the-search-queue)) |
| `RateLimiting:Policies` `search` | 60 per minute | the policy search routes such as suggestions use |
<!--#if (sample)-->
| `Catalog:Search:UseSearchIndex` | true | Catalog answers text searches and facets from the index; `false` keeps every product query in SQL |
| `Catalog:Search:MaxWindow` | 1000 | the deepest product result read from the index; deeper pages come from SQL |
<!--#endif-->

The Meilisearch settings and their Docker secrets are on [Meilisearch engine](Meilisearch.md#configuration); the
Docker overlays on [Docker Compose](../deployment/Docker-Compose.md).

## Related

[Search module](../Modules/Search.md), [Search operations](Operations.md).
