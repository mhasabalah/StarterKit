import type * as PageTree from 'fumadocs-core/page-tree';
import navigation from '../.generated/navigation.json';

/**
 * The module chart: every element is a real part of the documentation. Module orders mirror
 * `src/Shared/Shared.Core/Modularity/ModuleOrder.cs` (guarded by tests/chart.test.mjs); page counts are
 * computed from the synced navigation, so a tile never claims a page that does not exist.
 */
export type Family = 'foundations' | 'connected' | 'samples' | 'practices';
export type ChartElement = {
  symbol: string; name: string; family: Family; url: string;
  /** URL prefixes (with trailing slash semantics handled by `matches`) whose pages belong to this element. */
  prefixes: string[]; order?: number; pages: number;
};
type ChartPage = { url: string; title: string };

export const families: { id: Family; name: string; description: string }[] = [
  { id: 'foundations', name: 'Foundations', description: 'Users, tenants, files and billing every product needs' },
  { id: 'connected', name: 'Connected', description: 'Events, deliveries, jobs and agents that reach outside a request' },
  { id: 'samples', name: 'Samples', description: 'Reference slices to copy from' },
  { id: 'practices', name: 'Practices', description: 'Rules and guides that apply to every module' },
];

type Definition = Omit<ChartElement, 'pages'>;
const moduleDefinitions: Definition[] = [
  { symbol: 'Id', name: 'Identity', family: 'foundations', order: 0, url: '/identity/', prefixes: ['/identity/'] },
  { symbol: 'Tn', name: 'Tenancy', family: 'foundations', order: 100, url: '/modules/tenancy/', prefixes: ['/modules/tenancy/'] },
  { symbol: 'At', name: 'Attachment', family: 'foundations', order: 200, url: '/attachment/', prefixes: ['/attachment/'] },
  { symbol: 'Ct', name: 'Catalog', family: 'samples', order: 300, url: '/modules/catalog/', prefixes: ['/modules/catalog/'] },
  { symbol: 'Sb', name: 'Subscriptions', family: 'foundations', order: 400, url: '/modules/subscriptions/', prefixes: ['/modules/subscriptions/', '/subscriptions/'] },
  { symbol: 'Wh', name: 'Webhooks', family: 'connected', order: 500, url: '/modules/webhooks/', prefixes: ['/modules/webhooks/'] },
  { symbol: 'Dt', name: 'DataTransfer', family: 'connected', order: 600, url: '/modules/datatransfer/', prefixes: ['/modules/datatransfer/'] },
  { symbol: 'Sh', name: 'Shipping', family: 'samples', order: 700, url: '/modules/shipping/', prefixes: ['/modules/shipping/'] },
  { symbol: 'Ag', name: 'Agentic', family: 'connected', order: 800, url: '/modules/agentic/', prefixes: ['/modules/agentic/'] },
  { symbol: 'Nt', name: 'Notification', family: 'connected', order: 900, url: '/modules/notification/', prefixes: ['/modules/notification'] },
];
const practiceDefinitions: Definition[] = [
  { symbol: 'Gs', name: 'Start here', family: 'practices', url: '/getting-started/new-project/', prefixes: ['/overview/', '/getting-started/', '/reference/features/'] },
  { symbol: 'Ar', name: 'Architecture', family: 'practices', url: '/architecture/overview/', prefixes: ['/architecture/', '/shared/'] },
  { symbol: 'Mt', name: 'Multi-tenancy', family: 'practices', url: '/multitenancy/overview/', prefixes: ['/multitenancy/'] },
  { symbol: 'Dp', name: 'Deployment', family: 'practices', url: '/deployment/', prefixes: ['/deployment/'] },
  { symbol: 'If', name: 'Infrastructure', family: 'practices', url: '/infrastructure/aspire/', prefixes: ['/infrastructure/'] },
  { symbol: 'Ts', name: 'Testing', family: 'practices', url: '/testing/testing-strategy/', prefixes: ['/testing/'] },
  { symbol: 'Rf', name: 'Reference', family: 'practices', url: '/reference/', prefixes: ['/reference/'] },
  { symbol: 'Dc', name: 'Decisions', family: 'practices', url: '/decisions/', prefixes: ['/decisions/'] },
];

const slash = (url: string) => (url.endsWith('/') ? url : `${url}/`);
function flatten(nodes: PageTree.Node[], out: ChartPage[] = []) {
  for (const node of nodes) {
    if (node.type === 'page') out.push({ url: slash(node.url), title: String(node.name) });
    else if (node.type === 'folder') {
      if (node.index) out.push({ url: slash(node.index.url), title: String(node.index.name) });
      flatten(node.children, out);
    }
  }
  return out;
}
const allPages = flatten(navigation as PageTree.Node[]);

/** Longest prefix wins, so `/reference/features/` belongs to Start here, not Reference. */
function owner(url: string, definitions: Definition[]) {
  let best: Definition | undefined; let length = -1;
  for (const definition of definitions) for (const prefix of definition.prefixes) {
    if (slash(url).startsWith(prefix) && prefix.length > length) { best = definition; length = prefix.length; }
  }
  return best;
}
const everything = [...moduleDefinitions, ...practiceDefinitions];
const count = (definition: Definition) => allPages.filter((page) => owner(page.url, everything) === definition).length;
const withPages = (definitions: Definition[]) => definitions.map((definition) => ({ ...definition, pages: count(definition) }))
  .filter((element) => element.pages > 0);

export const modules: ChartElement[] = withPages(moduleDefinitions);
export const practices: ChartElement[] = withPages(practiceDefinitions);

/** The element a documentation page belongs to, for its header tile. */
export function elementFor(url: string): ChartElement | undefined {
  const definition = owner(url, everything);
  return definition && [...modules, ...practices].find((element) => element.symbol === definition.symbol);
}

