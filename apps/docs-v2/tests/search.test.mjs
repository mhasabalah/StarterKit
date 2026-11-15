import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { searchAPI } from '../lib/search.ts';

test('independent search index covers articles and real API references', async () => {
  const indexes = JSON.parse(await readFile('.generated/search-index.json', 'utf8'));
  const inventory = JSON.parse(await readFile('.generated/inventory.json', 'utf8'));
  const prefix = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
  for (const page of inventory.pages) assert.ok(indexes.some((entry) => entry.url === prefix + page.newPath), page.newPath);
  assert.equal(new Set(indexes.map((entry) => entry.url)).size, indexes.length);
  const { GET } = searchAPI;
  for (const query of ['tenant', 'authenticator']) {
    const response = await GET(new Request(`http://localhost/api/search?query=${query}`));
    assert.equal(response.status, 200);
    assert.ok((await response.json()).length > 0, query);
  }
  assert.ok(indexes.some((entry) => entry.url.includes('/api-reference/v1/')));
  const results = await searchAPI.search('authentication', { tag: 'api' });
  assert.ok(results.length > 0);
  assert.ok(results.every((result) => result.url.includes('/api-reference/v1/')));
});
