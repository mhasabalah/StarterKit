import type { MetadataRoute } from 'next';
import { source } from '@/lib/source';
import { absoluteUrl } from '@/lib/site';
export default function sitemap(): MetadataRoute.Sitemap {
  return [{ url: absoluteUrl('/') }, ...source.getPages().map((page) => ({ url: absoluteUrl(page.url + '/'), lastModified: page.type === 'docs' ? page.data.lastUpdated || undefined : undefined }))];
}
