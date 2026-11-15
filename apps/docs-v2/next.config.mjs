import { createMDX } from 'fumadocs-mdx/next';

const basePath = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
if (basePath && !/^\/[a-zA-Z0-9/_-]+$/.test(basePath)) throw new Error('Invalid NEXT_PUBLIC_DOCS_BASE');
export default createMDX()({
  agentRules: false,
  output: 'standalone',
  basePath,
  trailingSlash: true,
  reactStrictMode: true,
  poweredByHeader: false,
  outputFileTracingIncludes: { '/*': ['./openapi/v1.json', './content/home.mdx', './content/docs/**/*.md', './.generated/search-index.json'] },
  experimental: { cpus: 2 },
  async headers() {
    return [{ source: '/:path*', headers: [
      { key: 'X-Content-Type-Options', value: 'nosniff' },
      { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
      ...(process.env.DOCS_PRODUCTION === 'true' ? [] : [{ key: 'X-Robots-Tag', value: 'noindex, nofollow' }]),
    ] }];
  },
});
