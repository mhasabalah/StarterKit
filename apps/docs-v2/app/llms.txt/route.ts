import { source } from '@/lib/source';
import { absoluteUrl } from '@/lib/site';
export const dynamic = 'force-static';
export function GET() {
  const links = source.getPages().map((page) => `- [${page.data.title}](${absoluteUrl(`/llms.mdx/${page.slugs.join('/')}`)}): ${page.data.description || ''}`);
  return new Response(`# StarterKit\n\n> ASP.NET Core modular monolith documentation.\n\n## Documentation\n\n- [Home](${absoluteUrl('/llms.mdx/home')})\n${links.join('\n')}\n`, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
}
