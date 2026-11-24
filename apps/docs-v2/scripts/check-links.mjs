import { readFile } from 'node:fs/promises';
import { load } from 'cheerio';
import { tools } from '../lib/tools.mjs';
const origin = process.env.DOCS_TEST_URL || 'http://localhost:3000';
const prefix = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const inventory = JSON.parse(await readFile('.generated/inventory.json', 'utf8'));
// Tool pages read their hash as state (#guided=agent, #section=Messaging), not as an element id: the page itself is
// still checked, its hash is not.
const statefulHash = new Set(tools.map((tool) => prefix + tool.url));
const documents = new Map();
const failures = [];
async function get(url) {
  if (!documents.has(url)) documents.set(url, (async () => {
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        const response = await fetch(url, { signal: AbortSignal.timeout(30000) });
        if (!response.ok) failures.push(`${response.status} ${url}`);
        const html = response.headers.get('content-type')?.includes('text/html') ?? false;
        if (!html) { await response.body?.cancel(); return { html: false, ids: new Set(), links: [] }; }
        const $ = load(await response.text());
        // Retain small metadata, not a parsed DOM for every API reference page.
        return { html: true, ids: new Set($('[id]').toArray().map((element) => $(element).attr('id'))),
          links: $('a[href], img[src]').toArray().map((element) => $(element).attr('href') || $(element).attr('src')) };
      } catch (error) {
        if (attempt === 2) throw new Error(`Unable to check ${url}`, { cause: error });
        await new Promise((resolve) => setTimeout(resolve, 250 * (attempt + 1)));
      }
    }
  })());
  return documents.get(url);
}
for (const page of inventory.pages) {
  const url = new URL(prefix + page.newPath, origin);
  const document = await get(url.href);
  for (const target of document.links) {
    const link = new URL(target, url);
    if (link.origin !== url.origin) continue;
    const anchor = decodeURIComponent(link.hash.slice(1)); link.hash = '';
    const result = await get(link.href);
    if (anchor && result.html && !statefulHash.has(link.pathname) && !result.ids.has(anchor)) failures.push(`${page.newPath}: missing anchor ${target}`);
  }
  for (const heading of page.headings || []) if (!document.ids.has(heading.id)) failures.push(`${page.newPath}: missing original heading ${heading.id}`);
}
if (failures.length) throw new Error([...new Set(failures)].join('\n'));
console.log(`Validated links, assets and original heading anchors across ${inventory.pageCount} pages.`);
