export const basePath = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
export const siteUrl = process.env.DOCS_SITE || 'http://localhost:3000';
export const production = process.env.DOCS_PRODUCTION === 'true';
if (production && !process.env.DOCS_SITE) throw new Error('DOCS_SITE is required for production publication');
export const repository = process.env.GITHUB_REPOSITORY || 'mhasabalah/StarterKit';
export const publicPath = (url: string) => basePath && (url === basePath || url.startsWith(`${basePath}/`)) ? url : `${basePath}${url}`;
export const absoluteUrl = (url: string) => new URL(publicPath(url), siteUrl).href;
