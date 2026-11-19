import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { docs } from '@/.source';
import { loader } from 'fumadocs-core/source';
import type * as PageTree from 'fumadocs-core/page-tree';
import navigation from '../.generated/navigation.json';
import { openapi } from './openapi';
import { prepareNavigation } from './navigation';

const apiPages = await openapi.staticSource({ baseDir: 'api-reference/v1', groupBy: 'tag' });
export const source = loader({ docs: docs.toFumadocsSource(), openapi: apiPages }, { baseUrl: '/', plugins: [openapi.loaderPlugin()] });
const apiTree = loader({ baseUrl: '/', source: apiPages }).pageTree;
let apiGroups = apiTree.children;
while (apiGroups.length === 1 && apiGroups[0].type === 'folder') apiGroups = apiGroups[0].children;

// Group folders are titled from tag slugs ("Api keys", "Enterprise sso"); restore the contract's own tag names.
type Operations = Record<string, Record<string, { tags?: string[] }>>;
const contract = JSON.parse(await readFile(path.join(process.cwd(), 'openapi/v1.json'), 'utf8')) as { paths: Operations };
const key = (name: string) => name.toLowerCase().replace(/[^a-z0-9]/g, '');
const tags = new Map(Object.values(contract.paths).flatMap((operations) => Object.values(operations))
  .flatMap((operation) => operation.tags ?? []).map((tag) => [key(tag), tag]));
apiGroups = apiGroups.map((node) => node.type === 'folder' && typeof node.name === 'string'
  ? { ...node, name: tags.get(key(node.name)) ?? node.name } : node);
export const pageTree: PageTree.Root = {
  $id: 'starterkit-documentation', name: 'Documentation',
  children: prepareNavigation([...navigation as PageTree.Node[], { type: 'folder', name: 'API reference v1', children: apiGroups }]),
};
