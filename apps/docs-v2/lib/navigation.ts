import { createElement } from 'react';
import type * as PageTree from 'fumadocs-core/page-tree';
import {
  Blocks, BookOpen, Boxes, Braces, Building2, CircleHelp, CreditCard, FileClock,
  FolderOpen, HardDrive, KeyRound, Layers3, Lightbulb, Network, Rocket, Server,
  Settings2, ShieldCheck, TestTubeDiagonal, Wrench,
  type LucideIcon,
} from 'lucide-react';

/** Chart symbols (see lib/chart.ts) for the sections and module pages that are elements of the module chart. */
const sectionChips: Record<string, [string, string]> = {
  'Start here': ['Gs', 'practices'], Architecture: ['Ar', 'practices'], 'Multi-tenancy': ['Mt', 'practices'],
  Identity: ['Id', 'foundations'], Attachments: ['At', 'foundations'], Subscriptions: ['Sb', 'foundations'],
  'Shared services': ['Ar', 'practices'], Deployment: ['Dp', 'practices'], Infrastructure: ['If', 'practices'],
  Testing: ['Ts', 'practices'], Reference: ['Rf', 'practices'], Decisions: ['Dc', 'practices'], 'API reference v1': ['Ap', 'api'],
  Notification: ['Nt', 'connected'],
  // Groups that are not chart elements carry a neutral chip, so every top-level entry reads the same way.
  Concepts: ['Co', 'api'], Modules: ['Mo', 'api'], Operations: ['Op', 'api'],
};
const moduleChips: Record<string, [string, string]> = {
  catalog: ['Ct', 'samples'], tenancy: ['Tn', 'foundations'], subscriptions: ['Sb', 'foundations'],
  notification: ['Nt', 'connected'], webhooks: ['Wh', 'connected'], datatransfer: ['Dt', 'connected'],
  agentic: ['Ag', 'connected'], shipping: ['Sh', 'samples'],
};
const chip = ([symbol, family]: [string, string]) => createElement('span', { className: 'element-chip', 'data-family': family, 'aria-hidden': true }, symbol);

const sectionIcons: Record<string, LucideIcon> = {
  'Start here': Rocket,
  Concepts: Lightbulb,
  Architecture: Layers3,
  'Multi-tenancy': Building2,
  Modules: Boxes,
  Identity: KeyRound,
  Attachments: FolderOpen,
  Subscriptions: CreditCard,
  'Shared services': Blocks,
  Operations: Settings2,
  Deployment: Rocket,
  Infrastructure: Server,
  Testing: TestTubeDiagonal,
  Reference: BookOpen,
  Decisions: FileClock,
  'API reference v1': Braces,
};

const moduleIcons: Record<string, LucideIcon> = {
  catalog: Boxes, tenancy: Building2, subscriptions: CreditCard,
  notification: Network, webhooks: Braces, datatransfer: HardDrive,
  agentic: Blocks, shipping: Boxes,
};

function pageChip(url: string) {
  if (!url.startsWith('/modules/')) return undefined;
  const entry = moduleChips[url.split('/')[2].split('-')[0]];
  return entry && chip(entry);
}

function pageIcon(url: string) {
  if (url.startsWith('/modules/')) return moduleIcons[url.split('/')[2].split('-')[0]] ?? Boxes;
  if (url === '/reference/endpoint-authorization') return ShieldCheck;
  if (url === '/reference/configuration') return Wrench;
  if (url.includes('troubleshooting')) return CircleHelp;
}

/** Startup order (ModuleOrder.cs) for the Modules group, so the sidebar reads like the home chart. */
const moduleOrder: Record<string, number> = {
  Identity: 0, Tenancy: 100, Attachments: 200, Catalog: 300, Subscriptions: 400, Webhooks: 500,
  DataTransfer: 600, Shipping: 700, Agentic: 800, Notification: 900,
};
const moduleKey: Record<string, string> = {
  catalog: 'Catalog', tenancy: 'Tenancy', subscriptions: 'Subscriptions', notification: 'Notification',
  webhooks: 'Webhooks', datatransfer: 'DataTransfer', agentic: 'Agentic', shipping: 'Shipping',
};
function keyOf(node: PageTree.Node) {
  if (node.type === 'page') return moduleKey[node.url.split('/')[2]?.split('-')[0] ?? ''];
  if (node.type === 'folder' && typeof node.name === 'string') return node.name;
}

/** One entry per module: a module's overview page becomes the index of its folder, and the list follows ModuleOrder. */
function regroupModules(children: PageTree.Node[]): PageTree.Node[] {
  const groups = new Map<string, PageTree.Node[]>();
  const rest: PageTree.Node[] = [];
  for (const node of children) {
    const key = keyOf(node);
    if (key && key in moduleOrder) groups.set(key, [...(groups.get(key) ?? []), node]);
    else rest.push(node);
  }
  const entries = [...groups.entries()].map(([key, nodes]) => {
    if (nodes.length === 1) return nodes[0];
    const folder = nodes.find((node): node is PageTree.Folder => node.type === 'folder');
    const pages = nodes.filter((node): node is PageTree.Item => node.type === 'page');
    const [index, ...others] = folder?.index ? [folder.index, ...pages] : pages;
    return { type: 'folder', name: key, index, children: [...others, ...(folder?.children ?? [])] } satisfies PageTree.Folder;
  });
  entries.sort((a, b) => moduleOrder[keyOf(a)!] - moduleOrder[keyOf(b)!]);
  return [...entries, ...rest];
}

/** Fumadocs matches normalized URLs; the public Next.js routes retain trailing slashes. */
export function prepareNavigation(nodes: PageTree.Node[], parent = 'docs', plain = false): PageTree.Node[] {
  nodes = nodes.map((node) => node.type === 'folder' && node.name === 'Modules' && parent === 'docs'
    ? { ...node, children: regroupModules(node.children) } : node);
  return nodes.map((node, position) => {
    const $id = `${parent}/${position}`;
    if (node.type === 'page') {
      const url = node.url.startsWith('/') ? node.url.replace(/\/$/, '') || '/' : node.url;
      const Icon = plain ? undefined : pageIcon(url);
      return { ...node, $id, url, icon: node.icon ?? (plain ? undefined : pageChip(url)) ?? (Icon && createElement(Icon, { 'aria-hidden': true })) };
    }
    if (node.type === 'folder') {
      // API tag groups are plain rows: their names repeat module names, but they are endpoints, not chart elements.
      const Icon = plain || typeof node.name !== 'string' ? undefined : sectionIcons[node.name] ?? FolderOpen;
      const sectionChip = !plain && typeof node.name === 'string' ? sectionChips[node.name] : undefined;
      const api = parent === 'docs' && node.name === 'API reference v1';
      return {
        ...node, $id, defaultOpen: false,
        icon: node.icon ?? (sectionChip ? chip(sectionChip) : Icon && createElement(Icon, { 'aria-hidden': true })),
        index: node.index ? prepareNavigation([node.index], `${$id}/index`, plain)[0] as PageTree.Item : undefined,
        children: prepareNavigation(node.children, $id, plain || api),
      };
    }
    return { ...node, $id };
  });
}
