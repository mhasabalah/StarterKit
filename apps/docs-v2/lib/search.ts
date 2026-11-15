import { readFileSync } from 'node:fs';
import path from 'node:path';
import { createSearchAPI, type AdvancedIndex } from 'fumadocs-core/search/server';

// JSON omits undefined heading fields; restore the structured-data contract explicitly.
const raw: AdvancedIndex[] = JSON.parse(readFileSync(path.join(process.cwd(), '.generated/search-index.json'), 'utf8'));
export const indexes: AdvancedIndex[] = raw.map((entry) => ({
  ...entry,
  tag: ['all', entry.url.includes('/api-reference/') ? 'api' : 'docs',
    ...(entry.url.includes('/architecture/') ? ['architecture'] : []),
    ...(entry.url.includes('/getting-started/') ? ['getting-started'] : [])],
  structuredData: { ...entry.structuredData, contents: entry.structuredData.contents.map((item) => ({
    ...item, heading: 'heading' in item ? item.heading : undefined,
  })) },
}));
export const searchAPI = createSearchAPI('advanced', { indexes });
