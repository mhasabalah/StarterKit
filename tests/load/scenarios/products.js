import { sleep } from 'k6';
import http from 'k6/http';
import { authHeaders, data, get, ok, post, url } from '../lib/http.js';
import { pick, seedData } from '../lib/seed-data.js';

// Product CRUD as an authenticated member (bearer token): create -> read -> update (optimistic
// concurrency Version) -> list mine -> delete.
export function productsScenario() {
  const user = pick(seedData().users);
  const params = { headers: authHeaders({ accessToken: user.accessToken }) };
  const sku = `LT-${__VU}-${__ITER}-${Date.now().toString(36)}`.toUpperCase();
  const body = (price) => ({
    name: { en: `Load product ${sku}`, ar: 'منتج تحميل' },
    description: 'k6 CRUD',
    sku,
    type: 'Physical',
    price,
    currency: 'USD',
  });

  const created = post('/api/v1/products', body(25), params);
  const id = (data(created) || {}).id;
  if (!ok(created, 'create product') || !id) return;

  const read = get(`/api/v1/products/${id}`, params);
  const version = (data(read) || {}).version;
  ok(read, 'read product');

  const update = http.put(url(`/api/v1/products/${id}`), JSON.stringify(Object.assign(body(30), { version })), params);
  ok(update, 'update product');

  ok(get('/api/v1/products/my?pageNumber=1&pageSize=10', params), 'list my products');

  ok(http.del(url(`/api/v1/products/${id}`), null, params), 'delete product');
  sleep(0.1);
}
