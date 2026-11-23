import { source } from './source';
import { absoluteUrl, publicPath, repository } from './site';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import matter from 'gray-matter';
import { outline } from './architecture-outline.mjs';

type View = Parameters<typeof outline>[0] & { id: string };
let views: Promise<Map<string, View>> | undefined;
// Architecture views (ADR-0035) become text outlines: agents and plain-text readers get the elements and steps, not an id.
async function withArchitectureOutlines(content: string) {
  if (!content.includes('```likec4-view')) return content;
  views ??= readFile(path.join(process.cwd(), '.generated/architecture-views.json'), 'utf8')
    .then((text) => new Map((JSON.parse(text).views as View[]).map((view) => [view.id, view])));
  const known = await views;
  return content.replace(/```likec4-view\n([^\n`]+)\n```/g, (fence, id: string) => {
    const view = known.get(id.trim());
    return view ? outline(view) : fence;
  });
}

// Generated Markdown is already portable, with original Mermaid fences and no executable JSX.
export async function markdownPage(slugs: string[]) {
  const page = source.getPage(slugs);
  if (!page) return undefined;
  if (page.type === 'openapi') {
    const schema = page.data.getSchema().bundled;
    const operations = page.data.getOpenAPIPageProps().operations || [];
    return `# ${page.data.title}\n\n${absoluteUrl(page.url)}\n\nGenerated from the real ASP.NET Core contract. Full schemas: ${absoluteUrl('/api-reference/v1/openapi.json')}\n\n` + operations.map(({ path, method }) => `## ${method.toUpperCase()} ${path}\n\n\`\`\`json\n${JSON.stringify(schema.paths?.[path]?.[method], null, 2)}\n\`\`\`\n`).join('\n');
  }
  const file = path.join(process.cwd(), 'content/docs', page.path);
  const { content } = matter(await readFile(file, 'utf8'));
  return `# ${page.data.title}\n\n${absoluteUrl(page.url)}\n\nSource: https://github.com/${repository}/blob/master/${page.data.source}\n\n${await withArchitectureOutlines(content)}`;
}
export async function homeMarkdown() {
  const content = await readFile(path.join(process.cwd(), 'content/home.mdx'), 'utf8');
  return content.replace(/\]\((\/[^)]*)\)/g, (_, url) => `](${publicPath(url)})`);
}
export const markdownHeaders = { 'Content-Type': 'text/markdown; charset=utf-8', 'Cache-Control': 'public, max-age=3600' };
