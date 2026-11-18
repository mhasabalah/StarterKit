import type { Root } from 'mdast';
import { visit } from 'unist-util-visit';

/** Convert only diagram fences; never interpret diagram source as JSX. */
export function remarkMermaid() {
  return (tree: Root) => {
    visit(tree, 'code', (node, index, parent) => {
      if (node.lang !== 'mermaid' || index === undefined || !parent) return;
      parent.children[index] = {
        type: 'mdxJsxFlowElement', name: 'Mermaid',
        attributes: [{ type: 'mdxJsxAttribute', name: 'code', value: node.value }],
        children: [],
      } as unknown as typeof node;
    });
  };
}
