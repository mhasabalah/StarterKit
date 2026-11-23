# 0036. Put full-text search in one Search module, behind an engine port, fed by the outbox

Any module can make an entity searchable by registering an index and raising one event. The engine (PostgreSQL by
default, Meilisearch by choice) can be swapped without touching the modules.

## Status

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Every list search in the template was `ILIKE '%term%'`: Catalog products, Identity users and admins, the Subscriptions
admin views. It has no index, so it scans; it has no ranking, stemming, typo tolerance or Arabic normalization; and
searching Arabic text written without hamza or with ة/ه swapped finds nothing. Backlog item G17 asked for a search
abstraction that a product can grow with, without making every product run a search cluster.

The forces:

- **Bilingual data.** Names are `LocalizedText` (`name_en` / `name_ar`). Arabic needs normalization (alef forms, ة,
  ى, diacritics, tatweel, Arabic-Indic digits); English benefits from stemming.
- **Tenant isolation.** Search results must never cross tenants or branches, and must not become the place that
  decides visibility: soft delete, status and permissions belong to the owning module.
- **Operations.** The default deployment is one VPS with Docker Compose. A JVM search cluster (Elasticsearch,
  OpenSearch) costs gigabytes of memory, host tuning and its own backups, and its licensing needs review. PostgreSQL is
  already there and already has full-text search, trigrams and RLS.
- **Growth.** Some products will outgrow PostgreSQL search (instant search over large catalogs, heavy faceting), so
  the modules must not depend on the engine.

## Decision

**One Search module, one port.**

- The Search module owns the index (schema `search`, a tenant-data context with forced RLS). Owning modules depend
  only on `Search.Contracts`, which is engine-neutral.
- Engines are adapters behind an internal `ISearchEngine`: `PostgresSearchEngine` (the default) and
  `MeilisearchSearchEngine`. `Search:Engine` selects one at startup and is validated on start; there is no silent
  fallback from one engine to another.
- Engine client packages stay in the Search module (architecture test). The Meilisearch adapter uses a typed
  `HttpClient`, not an SDK.
- The module is optional in generated products (`--search`, on by default).

**A module plugs in an index; it does not call an engine.**

- `services.AddSearchIndex<TSource>(definition)`. The definition names the index (`<module>.<entity>`, never renamed),
  its text fields (weight, analyzer: Simple, English, Arabic or Bilingual; whether they feed suggestions and
  highlights) and its typed filter fields (keyword, keyword list, number, date, boolean; facetable, sortable). The
  registry validates every definition at startup.
- The module implements `ISearchDocumentSource`: the current documents for a set of ids, and a stream of all of them
  for a rebuild. A document carries its id, branch, source version, text and filter values. Soft-deleted rows are
  never returned, so they are never indexed.

**The index follows the data through the outbox, by pulling.**

- An aggregate raises `SearchDocumentsChangedIntegrationEvent(IndexKey, DocumentIds)` with `AddIntegrationEvent`, in
  the same transaction as the change. It carries ids only.
- The Search consumer asks the source for the current state of those ids: present ids are upserted, absent ids are
  deleted. This is idempotent and order-independent, like every consumer.
- On PostgreSQL an upsert carries the source version and loses against a newer one already indexed.
- A rebuild streams the source under a lease and stamps every document, then sweeps documents the stamp did not reach.
  The old documents keep answering until the sweep, so a rebuild never takes search down.
- A reconcile job rebuilds an index whose definition or normalizer changed (a fingerprint), and a daily drift job
  compares source and index versions and repairs missing, stale and orphaned documents.

**Search returns ids. The owner shows only what it would show anyway.**

- The query (`SearchDocumentsQuery`, sent through `ISender`) returns hits (id, score, highlights), the total, facet
  counts and a status. It is a trusted, internal port: the owning module's endpoint decides who may search.
- Search always applies the tenant and the caller's branch scope itself: on PostgreSQL by naming the tenant in every
  statement and through RLS; on Meilisearch by a tenant and branch filter the adapter adds to every request.
- The owner loads the hits through its own DbContext and filters again (tenant, branch, soft delete, status), so a
  stale index entry is dropped and never shown. The owner always sends its visibility filters (for example
  `status=Published` for public product search), because totals and facet counts come from the index.
- When the index cannot answer (not built yet, engine down) the status says so instead of throwing, and the owner
  falls back to its SQL search. A first deploy with an empty index degrades to the old behaviour, not to an error.
- Lists people check right after editing (their own items, moderation queues, plain browsing without a term) stay on
  SQL, so eventual consistency is not visible there.

**Text is normalized once, by Search, for every engine.**

- The normalizer follows the Lucene Arabic normalization rules (strip diacritics and tatweel; أ إ آ ٱ → ا, ى → ي,
  ة → ه; Arabic-Indic digits → 0-9) and folds Latin case and diacritics. It runs on documents and on queries, keeps an
  offset map, and has a version that is part of the index fingerprint.
- One query parser (terms, quoted phrases, `-` exclusion, `OR`, a token cap) feeds every engine.
- Engines report match positions; Search builds the highlight snippets on the original text, so highlights look the
  same on every engine and show the text as the user wrote it.

**PostgreSQL engine.**

- One table, `search.search_documents`, keyed by tenant, index and document id: a `tsvector` built per field with its
  weight and text search configuration (`english`, `arabic`, `simple`), a trigram column (`pg_trgm`) for typo
  tolerance and suggestions, filter values in `jsonb`, and the text needed for highlights. GIN indexes on all three.
- Matching is full text first; prefix matching serves autocomplete; a trigram similarity search runs only when full
  text finds nothing and reports the result as fuzzy. Facets are counted over the filtered matches, with a scan cap.
- Every statement has a timeout. Field names are always parameters; only values from closed enums (configuration,
  weight, cast) appear in SQL text.

**Meilisearch engine.**

- One Meilisearch index per index key, shared by the tenants of the deployment; every document carries its tenant and
  branch, and the adapter adds them to every filter. Writes wait for the Meilisearch task, so a failure is retried by
  the consumer.
- Tenants on a dedicated database should use the PostgreSQL engine: Meilisearch is a shared external store, so it
  does not keep their text in their database.

**Operations and analytics.**

- Indexing jobs run on their own Hangfire queue, `search`.
- Tenant administrators see index status, rebuild an index and run a drift check (`search.manage`); platform operators
  see the engine and rebuild an index for every tenant (`search.operate`, SuperAdmin).
- Searches that find nothing are counted per tenant, index, day and normalized query, to find missing synonyms and
  products. No user id or IP is stored, query text is shortened, text that looks like an email or a long number is
  dropped, and the data expires (`Search:Analytics:RetentionDays`).

## Consequences

- Making an entity searchable is one definition, one source and one event raised by the aggregate. Swapping the
  engine is one configuration value; adding an engine is one adapter that passes the shared conformance test suite.
- Search is eventually consistent: a change is searchable seconds after it commits (the outbox and the consumer), not
  in the same request. Pages of results can be shorter than requested when stale entries are dropped by the owner.
- Totals and facet counts come from the index. An owner that forgets a visibility filter could leak counts (never
  rows); each owner builds its filters in one place, and a test pins it.
- PostgreSQL needs `pg_trgm` (a trusted extension; the role script creates it for hardened migration roles). Its
  search cost grows with the number of matches; very large indexes are the signal to move to Meilisearch.
- Meilisearch has no stemming and keeps the last write; the conformance suite marks such differences as engine
  capabilities instead of hiding them. Drift repair covers its missing version guard.
- Changing the normalizer or a definition rebuilds that index in every tenant, spread by the reconcile job.
- Enforced by: the registry's startup validation, the engine conformance suite (run on every engine), the architecture
  tests (messaging, tenant isolation, RLS coverage, third-party isolation, search index registrations) and the
  multi-tenant end-to-end tests.

## Alternatives considered

- **Elasticsearch or OpenSearch as the default.** The most capable option, but it adds a cluster to every product,
  isolates tenants only by the filters the application remembers to add, and needs its own backups. It remains a
  possible adapter behind the same port.
- **A `tsvector` column on each module's own table.** Always current and the least machinery, but PostgreSQL-only:
  moving to an external engine later would mean rewriting every module's search. Each module would also repeat the
  normalization, ranking and highlighting.
- **Pushing documents in the event.** Events would carry content (against the messaging rules), and an out-of-order
  delivery could index old text. Pulling the current state makes ordering irrelevant.
- **Synchronous indexing in the command.** A search outage would fail writes, and a crash after the commit would
  lose the update. The outbox decouples them.
- **Index-level permissions.** Public pages search anonymously; the owning endpoint already decides who may search, and
  the owner re-checks every hit. A permission on the index would duplicate that decision in a second place.

## Related

[ADR index](README.md), [Architecture overview](../Architecture/Overview.md),
[ADR-0004 outbox](0004-outbox-and-messaging-transports.md), [ADR-0005 tenant isolation](0005-tenant-isolation-model.md),
[ADR-0016 bulk data transfer](0016-bulk-data-transfer-on-hangfire.md) (the definition plug-in pattern), and
`/starterkit:update-docs`.
