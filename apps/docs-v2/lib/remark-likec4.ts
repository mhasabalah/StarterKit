import type { Root } from 'mdast';
import { visit } from 'unist-util-visit';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { repository } from './site';

type ViewSummary = { id: string; title: string; description: string; source?: string; width: number; height: number };

function views(): Map<string, ViewSummary> {
  const file = path.join(process.cwd(), '.generated/architecture-views.json');
  const summary = JSON.parse(readFileSync(file, 'utf8')) as { views: ViewSummary[] };
  return new Map(summary.views.map((view) => [view.id, view]));
}

/**
 * A ```likec4-view fence names one view of the architecture model (wiki/Architecture/model, ADR-0035). It becomes an
 * <ArchitectureView>; the fence body is a view id, never markup. The sync step has already rejected unknown ids.
 */
export function remarkLikeC4() {
  return (tree: Root) => {
    let known: Map<string, ViewSummary> | undefined;
    visit(tree, 'code', (node, index, parent) => {
      if (node.lang !== 'likec4-view' || index === undefined || !parent) return;
      known ??= views();
      const id = node.value.trim();
      const view = known.get(id);
      if (!view) throw new Error(`Unknown architecture view "${id}"`);
      const attribute = (name: string, value: string) => ({ type: 'mdxJsxAttribute', name, value });
      parent.children[index] = {
        type: 'mdxJsxFlowElement', name: 'ArchitectureView',
        attributes: [attribute('id', id), attribute('title', view.title), attribute('description', view.description),
          attribute('source', view.source ? `https://github.com/${repository}/blob/master/${view.source}` : ''), attribute('width', String(view.width))],
        children: [],
      } as unknown as typeof node;
    });
  };
}
