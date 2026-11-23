// Shared by the sync step, tests and the Markdown served to agents (lib/markdown.ts); no Node or bundler dependencies.

/** Plain-text outline of a view, for Markdown served to agents and readers without JavaScript. */
export function outline(view) {
  const names = new Map(view.nodes.map((node) => [node.id, node.title]));
  const steps = view.edges.map((edge, index) => `${view.type === 'dynamic' ? `${index + 1}.` : '-'} ${names.get(edge.source) ?? edge.source} → ${names.get(edge.target) ?? edge.target}${edge.label ? `: ${edge.label}` : ''}${edge.technology ? ` (${edge.technology})` : ''}`);
  return `**Architecture view: ${view.title}.** ${view.description}\n\n${view.type === 'dynamic' ? 'Steps:' : 'Elements: ' + view.nodes.map((node) => node.title).join(', ') + '.\n\nRelationships:'}\n\n${steps.join('\n')}`;
}
