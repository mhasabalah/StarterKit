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
export const pageTree: PageTree.Root = {
  $id: 'starterkit-documentation', name: 'Documentation',
  children: prepareNavigation([...navigation as PageTree.Node[], { type: 'folder', name: 'API reference v1', children: apiGroups }]),
};
