# Search module

Search gives every module full-text search (ranking, stemming, Arabic normalization, typo tolerance, facets and
highlights) through one engine-neutral contract. PostgreSQL is the default engine; Meilisearch is an option.

Search is the **index**. The module that owns the data plugs in an **index definition** and a **document source**,
so Search never references an owning business module. Design rationale:
[ADR-0036](../Decisions/0036-search-module-and-engine-port.md).

Data plane (`ITenantDataContext`, schema `search`): every row carries `tenant_id` and has forced RLS.

## Status

The contract, the index registry, the query validation and the configuration are in place. The engines are not
registered yet, so every search reports `IndexNotReady` and owning modules keep their SQL search. The PostgreSQL
engine, the indexing pipeline, the admin API, the analytics and the Meilisearch adapter follow.

## Layout

```text
Search.Contracts/
  Indexing/   SearchIndexDefinition (text + filter fields), SearchDocument (+ builder), ISearchDocumentSource,
              AddSearchIndex<TSource>(definition)
              IntegrationEvents/SearchDocumentsChangedIntegrationEvent (search.documents-changed.v1)
  Querying/   SearchDocumentsQuery, filters (keyword, keywords, number/date range, bool), SearchDocumentsResult
Search/
  SearchModule.cs   Add/UseSearchModule, options, registry, startup checks
  Options/          SearchOptions (section "Search") + SearchOptionsValidator
  Engine/           SearchIndexRegistry, RegisteredIndex (field lookups, fingerprint, document checks)
  Domains/Querying/ SearchDocumentsQueryValidator + handler
Search.Tests/            contracts, registry, fingerprint, options, query validation
Search.IntegrationTests/ Testcontainers Postgres, real module, RLS app role
```

## Making an entity searchable

1. **Define the index** in the owning module: a key `<module>.<entity>` (never renamed), text fields with a weight and
   an analyzer (`Simple`, `English`, `Arabic`, `Bilingual`), and typed filter fields (`Keyword`, `KeywordList`,
   `Number`, `Date`, `Boolean`; facetable, sortable).

   ```csharp
   public static readonly SearchIndexDefinition Definition = new()
   {
       Key = "orders.orders",
       Name = LocalizedText.Create("Orders", "الطلبات"),
       TextFields =
       [
           new SearchTextField("number", SearchWeight.Highest, SearchAnalyzer.Simple) { Suggest = true },
           new SearchTextField("customer", SearchWeight.High, SearchAnalyzer.Bilingual) { Highlight = true }
       ],
       FilterFields =
       [
           new SearchFilterField("status", SearchFieldType.Keyword) { Facetable = true },
           new SearchFilterField("total", SearchFieldType.Number) { Sortable = true }
       ]
   };
   ```

2. **Implement the source** (`ISearchDocumentSource`): the current documents for a set of ids, and a stream of all of
   them. Read every branch (`IgnoreBranchScope()`), never return soft-deleted entities, and build each document with
   `SearchDocument.For(entity.Id, entity.Version)...Build()`.
3. **Register both**: `services.AddSearchIndex<OrderSearchSource>(OrderSearchIndex.Definition);` in `Add<M>Module`.
   The host refuses to start when a definition is invalid or duplicated.
4. **Raise the change** from the aggregate whenever a searchable value changes, including delete and restore:
   `AddIntegrationEvent(SearchDocumentsChangedIntegrationEvent.For(OrderSearchIndex.Definition.Key, Id));`. It goes
   through the outbox; Search reads the current state from the source.
5. **Search** from the owning module's handler with `SearchDocumentsQuery` (through `ISender`), always with its
   visibility filters. Load the hits through the module's own DbContext and filter them again. When `Status` is not
   `Ok`, fall back to the module's SQL search.

## Configuration

Section `Search` (validated at startup):

| Key | Default | Meaning |
|---|---|---|
| `Engine` | `Postgres` | `Postgres` or `Meilisearch` |
| `Query:MaxPageSize` / `MaxWindow` / `MaxTextLength` / `MaxTerms` | 100 / 1000 / 200 / 10 | query limits |
| `Query:Fuzzy:Enabled` / `MinTermLength` / `Threshold` | true / 3 / 0.4 | close-spelling fallback |
| `Facets:MaxValues` / `ScanLimit` | 20 / 10000 | facet counts |
| `Postgres:StatementTimeoutMs` | 2000 | timeout of every search query |
| `Meilisearch:Url` / `ApiKey` / `IndexPrefix` | | required with `Engine=Meilisearch` (the key from a secret) |
| `Indexing:BatchSize` / `LeaseMinutes` | 500 / 30 | rebuilds |
| `Reconcile:Cron`, `Drift:Cron` / `AutoRepair` / `MaxDocuments` | every 15 min, 03:00 / true / 200000 | index upkeep |
| `Analytics:Enabled` / `RetentionDays` / `MaxQueryLength` / `FlushSeconds` | true / 90 / 64 / 30 | zero-result analytics |

## Related

[ADR-0036](../Decisions/0036-search-module-and-engine-port.md), [Messaging](../Architecture/Messaging.md),
[Multi-tenancy overview](../MultiTenancy/Overview.md).
