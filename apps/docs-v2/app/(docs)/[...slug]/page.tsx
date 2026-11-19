import { source } from '@/lib/source';
import { DocsPage, DocsBody, DocsTitle, DocsDescription, EditOnGitHub, PageLastUpdate, MarkdownCopyButton, ViewOptionsPopover } from 'fumadocs-ui/layouts/glass/page';
import { notFound } from 'next/navigation';
import { getMDXComponents } from '@/mdx-components';
import { absoluteUrl, publicPath, repository } from '@/lib/site';
import type { Metadata } from 'next';
import { OpenAPIPage } from '@/components/api-page';
import { ElementTile } from '@/components/element-tile';
import { CopyForAgent } from '@/components/agent-copy';
import { SectionBar } from '@/components/section-bar';
import { elementFor } from '@/lib/chart';

export const dynamicParams = false;
export function generateStaticParams() { return source.generateParams(); }
type Props = { params: Promise<{ slug: string[] }> };
export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const page = source.getPage((await params).slug);
  if (!page) notFound();
  return { title: page.data.title, description: page.data.description, alternates: { canonical: absoluteUrl(page.url + '/') } };
}
export default async function Page({ params }: Props) {
  const page = source.getPage((await params).slug);
  if (!page) notFound();
  if (page.type === 'openapi') return <DocsPage full toc={page.data.toc}>
    <SectionBar toc={page.data.toc} />
    <div className="page-head"><ElementTile api /><DocsTitle>{page.data.title}</DocsTitle></div>
    <DocsDescription>{page.data.description}</DocsDescription>
    <div className="page-tools"><CopyForAgent title={page.data.title ?? 'API reference'} path={`${page.url}/`} markdownUrl={publicPath(`/llms.mdx/${page.slugs.join('/')}`)} /><MarkdownCopyButton markdownUrl={publicPath(`/llms.mdx/${page.slugs.join('/')}`)} /><ViewOptionsPopover markdownUrl={publicPath(`/llms.mdx/${page.slugs.join('/')}`)} /></div>
    <p className="text-sm text-fd-muted-foreground mb-6">Generated from the ASP.NET Core OpenAPI contract. Generated examples are illustrative. Live requests are disabled.</p>
    <DocsBody><OpenAPIPage {...page.data.getOpenAPIPageProps()} /></DocsBody>
  </DocsPage>;
  const Content = page.data.body;
  const githubUrl = `https://github.com/${repository}/blob/master/${page.data.source}`;
  const markdownUrl = publicPath(`/llms.mdx/${page.slugs.join('/')}`);
  return <DocsPage toc={page.data.toc} full={page.data.full}>
    <SectionBar toc={page.data.toc} />
    <div className="page-head"><ElementTile element={elementFor(page.url)} /><DocsTitle>{page.data.title}</DocsTitle></div>
    <div className="page-tools"><CopyForAgent title={page.data.title} path={`${page.url}/`} markdownUrl={markdownUrl} /><MarkdownCopyButton markdownUrl={markdownUrl} /><ViewOptionsPopover markdownUrl={markdownUrl} githubUrl={githubUrl} /></div>
    <DocsBody>{page.slugs.join('/') === 'getting-started/new-project' && <span id="6-build-your-first-module" aria-hidden="true" />}<Content components={getMDXComponents()} /></DocsBody>
    <div className="page-tools"><EditOnGitHub href={githubUrl} />{page.data.lastUpdated && <PageLastUpdate date={new Date(page.data.lastUpdated)} />}</div>
  </DocsPage>;
}
