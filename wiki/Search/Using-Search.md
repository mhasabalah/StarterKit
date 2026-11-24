# Searching from a client

What a front end or an API client needs to know to build a search experience on StarterKit: what to send, what
comes back, and how to render it. Search itself has no public search route: **each owning module exposes its own**,
so the module decides who may search and what they see.

Part of the [Search module](../Modules/Search.md).

## What every search route has in common

- **One query box.** People type words, `"phrases"`, `-exclusions` and `OR` ([query syntax](Text-And-Languages.md#query-syntax)).
  Arabic and Latin text match however they were written (diacritics, alef forms, accents, case).
- **Best matches first** when no sort is given; a sort column, when the index can sort on it.
- **Close spellings** when nothing matches exactly; the response says so, so the UI can say "Showing results for
  similar words".
- **Facets**: counts per value over all matches (not just the page), for filter chips and sidebars.
- **Highlights**: snippets with matches in `<mark>`, HTML-encoded and safe to insert.
- **Always an answer.** When the index cannot answer (not built yet, engine down), the module's SQL search answers:
  the same records and paging, no facets or highlights, simpler matching. Clients never see an error for it.
- **Eventual.** A change is searchable seconds after it is saved. After an edit, show the saved record from its own
  route, not from a search.
<!--#if (sample)-->

## Product search

`GET /api/v1/products/search`, the reference route, in Catalog. Anonymous callers get page 1 only (`PageGate`); signed-in members get every page.
Results are published products only.

| Parameter | Meaning |
|---|---|
| `searchTerm` | The text typed (up to 200 characters). Empty browses with the filters |
| `facets` | Repeat for each: `Category`, `Tag`, `Type`, `Currency`, `Featured` |
| `highlight` | `true` for snippets per product |
| `type`, `currency`, `categoryId` (+ `includeSubcategories`), `tagIds`, `attributeTypeIds`, `ownerId`, `isFeatured`, `minPrice`, `maxPrice` | Filters, combined with AND (repeat `tagIds` for several; a product needs all of them) |
| `pageNumber`, `pageSize` | Paging (the index answers up to `Catalog:Search:MaxWindow` results deep; deeper pages come from SQL) |
| `sortColumn`, `sortBy` | `price`, `createdAt` or `publishedAt`, `Ascending` or `Descending`. No column: best matches first |

```http
GET /api/v1/products/search?searchTerm=lamps&facets=Type&facets=Category&highlight=true&pageNumber=1&pageSize=10
```

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "result": [ { "id": "6f1c...", "name": "Desk lamp", "sku": "LMP-01", "price": 25.0, "version": 3 } ],
    "pageInfoResult": { "currentPage": 1, "pageSize": 10, "totalItems": 14 },
    "match": "Exact",
    "facets": [
      { "facet": "Type", "values": [ { "value": "Physical", "count": 12 }, { "value": "Digital", "count": 2 } ] },
      { "facet": "Category", "values": [ { "value": "0b7e...", "count": 9 } ] }
    ],
    "facetsExhaustive": true,
    "highlights": {
      "6f1c...": { "nameEn": "Desk <mark>lamp</mark>", "description": "A warm <mark>lamp</mark> for long evenings" }
    }
  }
}
```

The product objects are the usual `ProductDto` (shortened above).

| Field | Meaning |
|---|---|
| `match` | `Exact`: the words matched. `Fuzzy`: nothing matched exactly; these are close spellings. `Browse`: no text. `Contains`: the SQL search answered (index not ready or not used for this request) |
| `facets` | In the order requested; values most frequent first. `Category` and `Tag` values are ids (resolve the names from the category tree and tag list); `Featured` is `true` or `false` |
| `facetsExhaustive` | `false` when the counts were taken over a capped number of matches (very broad queries) |
| `highlights` | Per product id, per field (`nameEn`, `nameAr`, `description`): the snippet. Only for fields that matched |

## Suggestions

`GET /api/v1/products/suggest`: as-you-type suggestions over published product names and SKUs. `q` is required; the last word is a prefix. `limit`
is 1 to 20 (default 8). Rate limited by the `search` policy; anonymous callers allowed.

```http
GET /api/v1/products/suggest?q=desk%20la&limit=5
```

```json
{ "isSuccess": true, "statusCode": 200,
  "data": [ { "id": "6f1c...", "name": "Desk lamp", "sku": "LMP-01", "highlight": "<mark>Desk</mark> <mark>la</mark>mp" } ] }
```

Names come in the request culture (`Accept-Language`). While the index is not available the list is empty, so the
box simply shows nothing.

## Building the UI

```ts
// As-you-type suggestions: debounce, cancel the previous request, ignore empty input.
let pending: AbortController | undefined;
let timer: ReturnType<typeof setTimeout> | undefined;

export function onSearchInput(text: string, show: (items: Suggestion[]) => void) {
  clearTimeout(timer);
  pending?.abort();
  if (!text.trim()) return show([]);
  timer = setTimeout(async () => {
    pending = new AbortController();
    const url = `/api/v1/products/suggest?q=${encodeURIComponent(text)}&limit=8`;
    try {
      const res = await fetch(url, { signal: pending.signal, headers: { 'Accept-Language': navigator.language } });
      if (res.ok) show((await res.json()).data);
    } catch { /* aborted by a newer keystroke */ }
  }, 150);
}

type Suggestion = { id: string; name: string; sku: string; highlight: string | null };
```

- **Render highlights as HTML** (they are encoded except for `<mark>`); fall back to the plain name or description
  when a product has no snippet. Style `mark` to fit your design.
- **Say what happened.** On `match: "Fuzzy"`, tell people the results are for similar words. On `match: "Contains"`,
  hide the facet panel (it is empty).
- **Facets narrow the next request.** Clicking a `Type` value sends `type=...`; a `Tag` value adds `tagIds=...`;
  keep the other facets requested so their counts update.
- **Submit, don't stream, full searches.** Send `/products/search` when people press Enter or pick a filter; keep
  `/suggest` for keystrokes. Only submitted searches are counted in the zero-result report.

## Errors

| Status | When |
|---|---|
| 400 | Text over 200 characters, an unknown facet, `q` missing or `limit` out of range (validation messages, localized) |
| 401 | Anonymous request for page 2 or later of `/products/search`, or a page size above the anonymous limit |
| 429 | `/suggest` over the `search` rate limit |

The index being unavailable is never an error: see `match: "Contains"` above.
<!--#endif-->

## For your own module

A module that adds its own index exposes its own search route the same way: it sends `SearchDocumentsQuery`, loads
the hits through its DbContext and falls back to SQL. The step-by-step guide is
[Adding a search index](Adding-A-Search-Index.md).

## Related

[Search module](../Modules/Search.md), [Text and languages](Text-And-Languages.md),
[How search works](How-It-Works.md).
<!--#if (sample)-->
The reference integration is [Catalog](../Modules/Catalog.md#search-index).
<!--#endif-->
