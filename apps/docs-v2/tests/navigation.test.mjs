import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { getBreadcrumbItems } from 'fumadocs-core/breadcrumb';
import { prepareNavigation } from '../lib/navigation.ts';

test('every migrated URL resolves through the sidebar, with or without its trailing slash', async () => {
  const navigation = JSON.parse(await readFile('.generated/navigation.json', 'utf8'));
  const tree = { name: 'Docs', children: prepareNavigation(navigation) };
  const ids = new Set();
  function check(nodes) {
    for (const node of nodes) {
      assert.ok(node.$id);
      assert.ok(!ids.has(node.$id), node.$id);
      ids.add(node.$id);
      if (node.type === 'folder') {
        assert.equal(node.defaultOpen, false);
        assert.ok(node.icon, node.name);
        check(node.children);
      } else if (node.type === 'page') {
        for (const url of [node.url, node.url + '/']) {
          const breadcrumbs = getBreadcrumbItems(url, tree, { includePage: true });
          assert.ok(breadcrumbs.length > 1, `Missing parent path: ${url}`);
          assert.equal(breadcrumbs.at(-1).url, node.url);
        }
      }
    }
  }
  check(tree.children);
});
