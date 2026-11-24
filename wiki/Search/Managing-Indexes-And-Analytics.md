# Managing indexes and search analytics

Tenants see and manage their own indexes and learn what their users search for and do not find; platform operators
see the engine and rebuild an index everywhere. Searching itself is not here: each owning module's search route
decides who may search.

Part of the [Search module](../Modules/Search.md).

## Managing indexes

Tenants manage their own indexes with the tenant permission `search.manage` (Owner and Administrator by default);
platform operators use `search.operate` (SuperAdmin only, host-only routes). Searching itself is not a Search route:
each owning module's search endpoint decides who may search.

| Route | Permission | What it does |
|---|---|---|
| `GET /api/v1/search/indexes` | `search.manage` | every registered index with its state in the tenant: status (`NotBuilt`, `Building`, `Ready`, `Failed`), up to date with the definition, documents, last build, last error, last drift check, fields |
| `GET /api/v1/search/indexes/{key}` | `search.manage` | one index; 404 for a key no module registers |
| `POST /api/v1/search/indexes/{key}/rebuild` | `search.manage`, not while impersonating | 202: queues a forced rebuild on the `search` queue; 409 `search_rebuild_running` while one holds the lease |
| `POST /api/v1/search/indexes/{key}/drift-check` | `search.manage`, not while impersonating | 202: queues a drift check of the index; the findings appear on the index. 409 `search_index_not_built` before the first build |
| `GET /api/v1/search/analytics/zero-results` | `search.manage` | the searches that found nothing (see below), paged |
| `GET /api/v1/search/analytics/daily` | `search.manage` | daily counts per index |
| `GET /api/v1/admin/search/engine` | `search.operate` | the configured engine and every definition with its fingerprint |
| `POST /api/v1/admin/search/indexes/{key}/rebuild-all` | `search.operate`, not while impersonating | 202: queues one forced rebuild per tenant (after an engine change or an incident; definition changes rebuild on their own) |

The enums the routes return (`SearchIndexStatus`, `SearchWeight`, `SearchAnalyzer`, `SearchFieldType`, `SearchStatus`,
`SearchMatchMode`) are served, localized, by the enum lookup endpoint.

## Search analytics

What people look for and do not find is the most useful search signal: it shows missing products, missing synonyms
and spellings the index does not handle. Search counts it without recording who searched.

- **What counts.** Only searches a person submitted: full mode, with text, first page. Autocomplete keystrokes,
  later pages and browsing by filters are not counted. Each counts once as found, zero results, rescued by close
  spellings (`Fuzzy`), or unanswered (index not built or engine down, so the owner's SQL search answered).
- **What is kept.** Per tenant, index and UTC day: the counts (`search_daily_stats`) and, for zero results, the
  normalized term with its count and last time (`search_zero_result_terms`). No user id, IP address or session. The
  term is normalized like the index (case, accents, Arabic letter forms), cut to `Analytics:MaxQueryLength`, and
  dropped when it contains `@` (emails, handles), six or more digits (phone, card, id and order numbers, also with
  spaces, dashes or dots) or is shorter than two characters (`SearchQueryScrubber`).
- **How it is written.** Each replica counts in memory (`SearchAnalyticsBuffer`, at most `Analytics:MaxPendingTerms`
  distinct terms between flushes; more are dropped and counted in `search.analytics.terms_dropped`), and
  `SearchAnalyticsFlushService` adds the counts to the tables every `Analytics:FlushSeconds` and when the host stops,
  one unit of work per tenant. A search never waits for it, and a failed write loses those counts only (fail open).
- **Retention.** `PurgeSearchAnalyticsJob` (`Analytics:PurgeCron`, daily) removes days older than
  `Analytics:RetentionDays`. `Analytics:Enabled=false` stops counting; the purge still runs.

## Related

[Search module](../Modules/Search.md), [Search operations](Operations.md),
[Endpoint authorization](../Reference/Endpoint-Authorization.md).
