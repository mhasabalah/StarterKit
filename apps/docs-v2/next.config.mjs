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
  outputFileTracingIncludes: { '/*': ['./openapi/v1.json', './content/home.mdx', './content/docs/**/*.md', './.generated/search-index.json', './.generated/architecture-views.json'] },
  experimental: { cpus: 2 },
  // The LikeC4 React bundle (architecture views) is built for Vite and reads `import.meta.env`; webpack does not
  // define it, so supply the three values the bundle uses.
  webpack(config, { webpack, dev }) {
    const mode = dev ? 'development' : 'production';
    config.plugins.push(new webpack.DefinePlugin({
      'import.meta.env.DEV': JSON.stringify(dev),
      'import.meta.env.MODE': JSON.stringify(mode),
      'import.meta.env': JSON.stringify({ DEV: dev, PROD: !dev, MODE: mode, SSR: false }),
    }));
    return config;
  },
  async headers() {
    return [{ source: '/:path*', headers: [
      { key: 'X-Content-Type-Options', value: 'nosniff' },
      { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
      ...(process.env.DOCS_PRODUCTION === 'true' ? [] : [{ key: 'X-Robots-Tag', value: 'noindex, nofollow' }]),
    ] }];
  },
});
