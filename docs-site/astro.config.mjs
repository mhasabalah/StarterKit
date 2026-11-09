import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import linksValidator from 'starlight-links-validator';
import { sidebar, siteBase, repository } from './sidebar.mjs';
import { readdir, mkdir, copyFile, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

// Assets live alongside generated Markdown, but must also be served verbatim.
// Run before Starlight's build-done link validation.
function wikiAssets() {
  const content = new URL('./src/content/docs/', import.meta.url);
  async function assets(directory = content, prefix = '') {
    const result = [];
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const name = prefix + entry.name;
      if (entry.isDirectory()) result.push(...await assets(new URL(entry.name + '/', directory), name + '/'));
      else if (!/\.mdx?$/i.test(name)) result.push(name);
    }
    return result;
  }
  return {
    name: 'wiki-assets',
    hooks: {
      'astro:build:done': async ({ dir }) => {
        for (const name of await assets()) {
          const destination = new URL(name, dir);
          await mkdir(fileURLToPath(new URL('.', destination)), { recursive: true });
          await copyFile(new URL(name, content), destination);
        }
      },
      'astro:server:setup': async ({ server }) => {
        const files = new Set(await assets());
        server.middlewares.use(async (request, response, next) => {
          let pathname;
          try { pathname = decodeURIComponent((request.url || '').split('?')[0]); }
          catch { return next(); }
          if (!pathname.startsWith(siteBase)) return next();
          const name = pathname.slice(siteBase.length);
          if (!files.has(name)) return next();
          try {
            response.setHeader('Content-Type', name.endsWith('.json') ? 'application/json' : 'application/octet-stream');
            response.end(await readFile(new URL(name, content)));
          } catch (error) { next(error); }
        });
      },
    },
  };
}

export default defineConfig({
  site: process.env.DOCS_SITE || undefined,
  base: siteBase,
  trailingSlash: 'always',
  integrations: [wikiAssets(), starlight({
    title: 'StarterKit',
    logo: { light: './src/assets/logo-light.svg', dark: './src/assets/logo-dark.svg', replacesTitle: true },
    favicon: '/favicon.svg', // Starlight prefixes its configured base.
    customCss: ['@fontsource/inter/400.css', '@fontsource/inter/500.css', '@fontsource/inter/600.css', '@fontsource/inter/700.css', '@fontsource/jetbrains-mono/400.css', './src/styles/brand.css'],
    components: { Hero: './src/components/BrandHero.astro' },
    social: [{ icon: 'github', label: 'GitHub', href: `https://github.com/${repository}` }],
    head: [
      { tag: 'meta', attrs: { property: 'og:image', content: process.env.DOCS_SITE ? new URL(`${siteBase}og-image.png`, process.env.DOCS_SITE).href : `${siteBase}og-image.png` } },
      { tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
      { tag: 'link', attrs: { rel: 'apple-touch-icon', href: `${siteBase}apple-touch-icon.png` } },
    ],
    sidebar,
    lastUpdated: true,
    editLink: { baseUrl: `https://github.com/${repository}/edit/master/` },
    plugins: [linksValidator({ failOnError: true, errorOnInvalidHashes: true, errorOnLocalLinks: false })],
  })],
});
