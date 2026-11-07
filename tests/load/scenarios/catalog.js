import { check, sleep } from 'k6';
import { get, headers, ok } from '../lib/http.js';
import { pick, seedData } from '../lib/seed-data.js';

// Public browse: anonymous, page 1 only (the "PageGate"), search and product detail.
export function catalogScenario() {
  const seed = seedData();
  const list = get('/api/v1/products?pageNumber=1&pageSize=10', { headers: headers() });
  ok(list, 'list products');

  const search = get(`/api/v1/products/search?searchTerm=${encodeURIComponent(seed.searchTerm)}&pageNumber=1&pageSize=10`, { headers: headers() });
  ok(search, 'search products', { 'search: has results': (r) => r.body.indexOf(seed.runId) !== -1 });

  const detail = get(`/api/v1/products/${pick(seed.productIds)}`, { headers: headers() });
  ok(detail, 'product detail');
  sleep(0.2);
}
