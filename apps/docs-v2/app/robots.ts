import type { MetadataRoute } from 'next';
import { production, absoluteUrl } from '@/lib/site';
export default function robots(): MetadataRoute.Robots {
  return { rules: { userAgent: '*', ...(production ? { allow: '/' } : { disallow: '/' }) }, ...(production ? { sitemap: absoluteUrl('/sitemap.xml') } : {}) };
}
