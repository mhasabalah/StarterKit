import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { unified } from 'unified';
import remarkParse from 'remark-parse';
import remarkGfm from 'remark-gfm';
import { visit } from 'unist-util-visit';
import matter from 'gray-matter';
import { pages, outputPath } from '../../../docs-site/sidebar.mjs';
import { renderTemplate } from '../scripts/template.mjs';
import { remarkCallouts } from '../lib/remark-callouts.ts';

const processor = unified().use(remarkParse).use(remarkGfm);
const hash = (text) => createHash('sha256').update(text).digest('hex');
const report = JSON.parse(await readFile('.generated/inventory.json', 'utf8'));

test('callout support preserves times and colon-prefixed prose', () => {
  const parser = unified().use(remarkParse).use(remarkCallouts);
  const tree = parser.parse('#### Purge at 03:00\n\n:::note\nKeep :identifiers literal.\n:::');
  assert.equal(tree.children[0].children[0].value, 'Purge at 03:00');
  assert.equal(tree.children[1].type, 'containerDirective');
  assert.equal(tree.children[1].children[0].children[0].value, 'Keep :identifiers literal.');
});
test('every V1 article plus homepage is accounted for without URL changes', () => {
  assert.equal(report.pages.length, pages.length + 1);
  assert.equal(new Set(report.pages.map((page) => page.newPath)).size, report.pages.length);
  assert.deepEqual(report.unresolved, []);
  for (const page of report.pages) assert.equal(page.oldPath, page.newPath);
});
test('migration preserves every code sample and diagram exactly', async () => {
  for (const page of pages) {
    const original = renderTemplate(await readFile(`../../wiki/${page.source}`, 'utf8'), page.source);
    const migrated = matter(await readFile(`content/docs/${outputPath(page.source)}`, 'utf8')).content;
    const blocks = (text) => {
      const result = [];
      visit(processor.parse(text), 'code', (node) => { result.push([node.lang, node.meta, hash(node.value)]); });
      return result;
    };
    assert.deepEqual(blocks(migrated), blocks(original), page.source);
  }
});
test('copied downloadable and brand assets match their sources', async () => {
  for (const asset of report.assets) assert.equal(hash(await readFile(`public${asset.path}`)), asset.hash, asset.path);
});
