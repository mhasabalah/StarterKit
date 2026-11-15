import { source } from '@/lib/source';
import { markdownPage, homeMarkdown, markdownHeaders } from '@/lib/markdown';
export const dynamic = 'force-static';
export const dynamicParams = false;
export function generateStaticParams() { return [...source.generateParams(), { slug: ['home'] }]; }
export async function GET(_request: Request, { params }: { params: Promise<{ slug: string[] }> }) {
  const { slug } = await params;
  const text = slug.join('/') === 'home' ? await homeMarkdown() : await markdownPage(slug);
  return new Response(text ?? 'Page not found', { status: text ? 200 : 404, headers: markdownHeaders });
}
