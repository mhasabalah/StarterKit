import type { MetadataRoute } from 'next';
import { source } from '@/lib/source';
import { absoluteUrl } from '@/lib/site';
import { tools } from '@/lib/tools.mjs';
export default function sitemap(): MetadataRoute.Sitemap {
  return [{ url: absoluteUrl('/') }, ...tools.map((tool) => ({ url: absoluteUrl(tool.url) })), ...source.getPages().map((page) => ({ url: absoluteUrl(page.url + '/'), lastModified: page.type === 'docs' ? page.data.lastUpdated || undefined : undefined }))];
}
