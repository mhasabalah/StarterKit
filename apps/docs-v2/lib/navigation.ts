import { createElement } from 'react';
import type * as PageTree from 'fumadocs-core/page-tree';
import {
  Blocks, BookOpen, Boxes, Braces, Building2, CircleHelp, CreditCard, FileClock,
  FolderOpen, HardDrive, KeyRound, Layers3, Lightbulb, Network, Rocket, Server,
  Settings2, ShieldCheck, TestTubeDiagonal, Wrench,
  type LucideIcon,
} from 'lucide-react';

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

function pageIcon(url: string) {
  if (url === '/overview') return BookOpen;
  if (url.startsWith('/getting-started/')) return Rocket;
  if (url.startsWith('/modules/')) return moduleIcons[url.split('/')[2].split('-')[0]] ?? Boxes;
  if (url === '/reference/endpoint-authorization') return ShieldCheck;
  if (url === '/reference/configuration') return Wrench;
  if (url.includes('troubleshooting')) return CircleHelp;
}

/** Fumadocs matches normalized URLs; the public Next.js routes retain trailing slashes. */
export function prepareNavigation(nodes: PageTree.Node[], parent = 'docs'): PageTree.Node[] {
  return nodes.map((node, position) => {
    const $id = `${parent}/${position}`;
    if (node.type === 'page') {
      const url = node.url.startsWith('/') ? node.url.replace(/\/$/, '') || '/' : node.url;
      const Icon = pageIcon(url);
      return { ...node, $id, url, icon: node.icon ?? (Icon && createElement(Icon, { 'aria-hidden': true })) };
    }
    if (node.type === 'folder') {
      const Icon = typeof node.name === 'string' ? sectionIcons[node.name] ?? FolderOpen : undefined;
      return {
        ...node, $id, defaultOpen: false,
        icon: node.icon ?? (Icon && createElement(Icon, { 'aria-hidden': true })),
        index: node.index ? prepareNavigation([node.index], `${$id}/index`)[0] as PageTree.Item : undefined,
        children: prepareNavigation(node.children, $id),
      };
    }
    return { ...node, $id };
  });
}
