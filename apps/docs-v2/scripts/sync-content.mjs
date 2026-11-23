import { readdir, readFile, writeFile, mkdir, copyFile, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { unified } from 'unified';
import remarkParse from 'remark-parse';
import remarkGfm from 'remark-gfm';
import remarkStringify from 'remark-stringify';
import { visit } from 'unist-util-visit';
import { toString } from 'mdast-util-to-string';
import GithubSlugger from 'github-slugger';
import { structure } from 'fumadocs-core/mdx-plugins/remark-structure';
import { createOpenAPI } from 'fumadocs-openapi/server';
import { sections, pages, pageSlug, outputPath, repository } from '../../../docs-site/sidebar.mjs';
import { renderTemplate } from './template.mjs';
import { buildArchitecture, modelRepoPath } from './architecture.mjs';

export const app = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(app, '../..');
const wiki = path.join(root, 'wiki');
const content = path.join(app, 'content/docs');
const generated = path.join(app, '.generated');
const processor = unified().use(remarkParse).use(remarkGfm).use(remarkStringify, { fences: true, bullet: '-', emphasis: '*', rule: '-' });
const hash = (value) => createHash('sha256').update(value).digest('hex');
const slash = (value) => value.split(path.sep).join('/');
const base = '/' + (process.env.NEXT_PUBLIC_DOCS_BASE || '').split('/').filter(Boolean).join('/');
const prefix = base === '/' ? '' : base;
const encode = (value) => value.split('/').map(encodeURIComponent).join('/');
const github = `https://github.com/${repository}`;

export async function walk(dir) {
  const result = [];
  for (const entry of (await readdir(dir, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
    if (entry.isSymbolicLink()) throw new Error(`Unsupported symlink: ${entry.name}`);
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) result.push(...await walk(full));
    else result.push(full);
  }
  return result;
}

export async function sync() {
  // The architecture model compiles first: pages may only name views it defines (ADR-0035).
  const architectureViews = await buildArchitecture();
  const viewIds = new Set(architectureViews.map((view) => view.id));
  const architectureUses = [];
  const files = await walk(wiki);
  const sources = files.filter((file) => /\.mdx?$/i.test(file)).map((file) => slash(path.relative(wiki, file)));
  const known = new Set(pages.map((page) => page.source));
  if (known.size !== pages.length) throw new Error('Duplicate source in navigation');
  const missing = sources.filter((source) => source !== 'INDEX.md' && !known.has(source));
  if (missing.length) throw new Error(`Pages missing from navigation: ${missing.join(', ')}`);
  const inventory = [];
  const searchIndexes = [];
  const outputs = new Map();
  const external = new Set();
  const allDiagrams = [];
  const publicAssets = [];
  const destinations = new Set();
  for (const entry of pages) {
    const source = entry.source;
    const raw = await readFile(path.join(wiki, source), 'utf8');
    // GFM requires pipes within table code spans to be escaped. Preserve the literal code content.
    const rendered = renderTemplate(raw, source).split('\n').map((line) => line.trimStart().startsWith('|')
      ? line.replace(/(`+)(.*?)\1/g, (match, ticks, code) => ticks + code.replace(/(?<!\\)\|/g, '\\|') + ticks) : line).join('\n');
    const tree = processor.parse(rendered);
    const firstHeading = tree.children.findIndex((node) => node.type === 'heading' && node.depth === 1);
    if (firstHeading < 0) throw new Error(`${source}: missing title`);
    const title = toString(tree.children[firstHeading]);
    const paragraph = tree.children.slice(firstHeading + 1).find((node) => node.type === 'paragraph');
    const description = paragraph ? toString(paragraph).replace(/\s+/g, ' ').slice(0, 160) : title;
    tree.children.splice(firstHeading, 1);
    const headings = [], assets = [], links = [], codes = [], dependencies = [];
    const slugger = new GithubSlugger();
    const pending = [];
    visit(tree, (node) => {
      if (node.type === 'heading') headings.push({ depth: node.depth, text: toString(node), id: slugger.slug(toString(node)) });
      if (node.type === 'code') {
        codes.push({ language: node.lang, hash: hash(node.value) });
        if (node.lang === 'mermaid') allDiagrams.push({ source, line: node.position.start.line, code: node.value });
        if (node.lang === 'likec4-view') {
          const id = node.value.trim();
          if (!viewIds.has(id)) throw new Error(`${source}: unknown architecture view "${id}" (define it in ${modelRepoPath})`);
          architectureUses.push({ source, line: node.position.start.line, view: id });
        }
      }
      if (node.type === 'html') {
        dependencies.push('html');
        if (/^<\/?[A-Z][A-Za-z0-9,]*>$/.test(node.value)) node.type = 'text';
        else if (!/^<!--[\s\S]*-->$/.test(node.value)) throw new Error(`${source}: unsupported HTML ${node.value}`);
      }
      if (['link', 'image', 'definition'].includes(node.type)) pending.push(node);
    });
    for (const node of pending) {
      const target = node.url;
      if (/^https?:/i.test(target)) { external.add(target); links.push(target); continue; }
      if (/^(?:[a-z][a-z\d+.-]*:|#|\/\/)/i.test(target)) { links.push(target); continue; }
      const [, pathname, suffix = ''] = target.match(/^([^?#]*)(.*)$/);
      const absolute = path.resolve(path.dirname(path.join(wiki, source)), decodeURIComponent(pathname));
      const relative = slash(path.relative(wiki, absolute));
      const modelSource = slash(path.relative(root, absolute)).startsWith(modelRepoPath);
      if (!modelSource && !relative.startsWith('../') && relative !== '..' && !path.isAbsolute(relative)) {
        if (/\.mdx?$/i.test(relative)) {
          if (relative !== 'INDEX.md' && !known.has(relative)) throw new Error(`${source}: unknown page ${target}`);
          node.url = prefix + (relative === 'INDEX.md' ? '/' : `/${pageSlug(relative)}/`) + suffix;
        } else {
          await stat(absolute);
          assets.push(relative);
          // Images are imported from public/ by the MDX compiler, and Next adds the base path to their URLs itself;
          // only plain links to assets need the prefix.
          node.url = `${node.type === 'image' ? '' : prefix}/${encode(relative.toLowerCase())}${suffix}`;
        }
      } else {
        const repoPath = slash(path.relative(root, absolute));
        if (repoPath.startsWith('../') || path.isAbsolute(repoPath)) throw new Error(`${source}: link escapes repository`);
        const info = await stat(absolute).catch(() => null);
        node.url = `${github}/${info?.isDirectory() ? 'tree' : 'blob'}/master/${encode(repoPath)}${suffix}`;
      }
      links.push(node.url);
    }
    const destination = outputPath(source);
    if (destinations.has(destination)) throw new Error(`Duplicate output: ${destination}`);
    destinations.add(destination);
    const updated = execFileSync('git', ['log', '-1', '--format=%cI', '--', `wiki/${source}`], { cwd: root, encoding: 'utf8' }).trim();
    const body = processor.stringify(tree);
    const url = `${prefix}/${pageSlug(source)}/`;
    searchIndexes.push({ id: url, url, title, description, structuredData: structure(body) });
    const frontmatter = { title, description, source: `wiki/${source}`, lastUpdated: updated || undefined };
    outputs.set(destination, `---\n${Object.entries(frontmatter).filter(([, value]) => value !== undefined).map(([key, value]) => `${key}: ${JSON.stringify(value)}`).join('\n')}\n---\n\n${body}`);
    inventory.push({ source: `wiki/${source}`, oldPath: `/${pageSlug(source)}/`, newPath: `/${pageSlug(source)}/`, title, description, status: 'converted', sourceHash: hash(raw), contentHash: hash(body), headings, assets, dependencies: [...new Set(dependencies)], codeBlocks: codes, links });
  }
  // Validate everything before replacing generated content. Removal is restricted to this app's generated directory.
  if (path.resolve(content) !== path.join(app, 'content', 'docs')) throw new Error('Unsafe generated directory');
  await rm(content, { recursive: true, force: true });
  await mkdir(generated, { recursive: true });
  for (const [destination, text] of outputs) {
    const file = path.join(content, destination);
    await mkdir(path.dirname(file), { recursive: true });
    await writeFile(file, text);
  }
  // Model sources are compiled into the views bundle, not published as assets.
  const isModelSource = (file) => slash(path.relative(root, file)).startsWith(`${modelRepoPath}/`);
  for (const file of files.filter((file) => !/\.mdx?$/i.test(file) && !isModelSource(file))) {
    const relative = slash(path.relative(wiki, file)).toLowerCase();
    const target = path.join(app, 'public', relative);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(file, target);
    publicAssets.push({ source: slash(path.relative(root, file)), path: `/${relative}`, hash: hash(await readFile(file)) });
  }
  for (const file of await walk(path.join(root, 'docs-site/public'))) {
    const relative = path.relative(path.join(root, 'docs-site/public'), file);
    if (/diagram-expand\.js$/.test(relative)) continue;
    const target = path.join(app, 'public', relative);
    await mkdir(path.dirname(target), { recursive: true });
    await copyFile(file, target);
    publicAssets.push({ source: slash(path.relative(root, file)), path: `/${slash(relative)}`, hash: hash(await readFile(file)) });
  }
  for (const name of ['logo-light.svg', 'logo-dark.svg']) {
    await copyFile(path.join(root, 'docs-site/src/assets', name), path.join(app, 'public', name));
  }
  const nav = sections.map(function convert(item) {
    return item.items ? { type: 'folder', name: item.label, children: item.items.map(convert) } : { type: 'page', name: item.label, url: `/${pageSlug(item.source)}/` };
  });
  inventory.unshift({ source: 'docs-site/src/content/docs/index.mdx', oldPath: '/', newPath: '/', title: 'StarterKit', status: 'converted-homepage', dependencies: ['Cards', 'Steps'], assets: publicAssets.map((asset) => asset.path) });
  const report = { schemaVersion: 1, pageCount: inventory.length, excluded: [{ source: 'wiki/INDEX.md', reason: 'Authoring index; represented by homepage and navigation, as in V1' }], pages: inventory, assets: publicAssets, redirects: [], unresolved: [] };
  searchIndexes.unshift({ id: prefix + '/', url: prefix + '/', title: 'StarterKit', structuredData: structure(await readFile(path.join(app, 'content/home.mdx'), 'utf8')) });
  const api = await createOpenAPI({ input: { v1: path.join(app, 'openapi/v1.json') } }).staticSource({ baseDir: 'api-reference/v1', groupBy: 'tag' });
  for (const file of api.files.filter((file) => file.type === 'page')) {
    const url = `${prefix}/${slash(file.path).replace(/\.mdx?$/, '')}/`;
    searchIndexes.push({ id: url, url, title: file.data.title, description: file.data.description, structuredData: file.data.structuredData });
  }
  await writeFile(path.join(generated, 'search-index.json'), JSON.stringify(searchIndexes) + '\n');
  await writeFile(path.join(generated, 'inventory.json'), JSON.stringify(report, null, 2) + '\n');
  await writeFile(path.join(generated, 'navigation.json'), JSON.stringify(nav, null, 2) + '\n');
  await writeFile(path.join(generated, 'diagrams.json'), JSON.stringify(allDiagrams, null, 2) + '\n');
  await writeFile(path.join(generated, 'architecture-uses.json'), JSON.stringify(architectureUses, null, 2) + '\n');
  await writeFile(path.join(generated, 'external-links.txt'), [...external].sort().join('\n') + '\n');
  console.log(`V2: ${inventory.length} pages, ${allDiagrams.length} Mermaid diagrams, ${architectureUses.length} architecture views, ${publicAssets.length} assets; no missing wiki pages.`);
  return report;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await sync();
