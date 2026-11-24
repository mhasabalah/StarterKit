import { Rate } from 'k6/metrics';
import { data, get, headers, ok } from '../lib/http.js';
import { seedData } from '../lib/seed-data.js';

// Share of full searches the search index answered (facets come only from the index; the SQL fallback has none).
// No threshold: a low value means the index was not built or the engine was down, and the SQL search answered.
export const searchFromIndex = new Rate('search_from_index');

// Full-text product search as an anonymous visitor (page 1, the "PageGate"): word forms with facets and highlights,
// as-you-type suggestions, a close spelling and an Arabic query. Needs the seeded products to be indexed (outbox ->
// Search consumer, seconds after the seed).
export function searchScenario() {
  const seed = seedData();
  const params = { headers: headers() };

  // "products" finds "product" (English stemming), only this run's products (the run id), with two facets.
  const full = get(
    `/api/v1/products/search?searchTerm=${encodeURIComponent(`seed products ${seed.runId}`)}` +
      '&facets=Type&facets=Currency&highlight=true&pageNumber=1&pageSize=10',
    params,
  );
  const page = data(full) || {};
  ok(full, 'search: full', { 'search: finds the seeded products': (r) => r.body.indexOf(seed.runId) !== -1 });
  searchFromIndex.add(Array.isArray(page.facets) && page.facets.length > 0);

  // Autocomplete: the last word is a prefix.
  const typed = get(`/api/v1/products/suggest?q=${encodeURIComponent(`${seed.runId} prod`)}&limit=8`, params);
  ok(typed, 'search: suggest', { 'search: suggests while typing': () => (data(typed) || []).length > 0 });

  // A close spelling (no exact match, so the fuzzy pass runs) and an Arabic query (normalized like the index).
  ok(get(`/api/v1/products/search?searchTerm=${encodeURIComponent('seed prodcut')}&pageNumber=1&pageSize=10`, params), 'search: close spelling');
  ok(get(`/api/v1/products/search?searchTerm=${encodeURIComponent('منتج')}&pageNumber=1&pageSize=10`, params), 'search: arabic');
}
