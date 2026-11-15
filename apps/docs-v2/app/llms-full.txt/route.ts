import { source } from '@/lib/source';
import { markdownPage, homeMarkdown } from '@/lib/markdown';
export const dynamic = 'force-static';
export async function GET() {
  const pages = await Promise.all(source.getPages().map((page) => markdownPage(page.slugs)));
  return new Response([await homeMarkdown(), ...pages].join('\n\n---\n\n'), { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
}
