// Builds .generated/config-catalog.json for the configuration builder (app/(home)/tools/config-builder) from the
// repository's real sources, so the tool cannot drift from them:
//   - src/Api/appsettings{,.Development,.Docker}.json, kept raw: the browser renders their template markers for the
//     symbols the reader picks (lib/config-builder/jsonc.mjs), exactly as `dotnet new` would;
//   - .template.config/template.json: the module and preset options of `dotnet new starterkit`;
//   - wiki/Reference/Configuration.md: every key's meaning and canonical guide;
//   - wiki/deployment/Secrets.md and docker-compose*.yml: which Docker secret supplies a key, and which file mounts it;
//   - docker-compose.yml and .env.example: the keys Compose sets through environment variables in the Docker target.
import { readFile, readdir, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { pages, pageSlug, repository } from '../../../docs-site/sidebar.mjs';
import { renderTemplate } from './template.mjs';
import { templateOptions } from '../lib/config-builder/overlay.mjs';

const app = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(app, '../..');
const slash = (value) => value.split(path.sep).join('/');
const github = `https://github.com/${repository}/blob/master/`;
const known = new Set(pages.map((page) => page.source));

/** Splits a Markdown table row on unescaped pipes. */
function cells(line) {
  return line.trim().replace(/^\||\|$/g, '').split(/(?<!\\)\|/).map((cell) => cell.trim().replace(/\\\|/g, '|'));
}
/** `[text](url)` → text; keeps inline code. */
const plain = (markdown) => markdown.replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/\s+/g, ' ').trim();
const codeSpans = (markdown) => [...markdown.matchAll(/`([^`]+)`/g)].map((match) => match[1]);

/** Resolves a link written in a wiki page to a docs route (wiki pages) or a GitHub URL (repository files). */
export function resolveLink(target, fromSource) {
  const [, pathname, suffix = ''] = target.match(/^([^?#]*)(.*)$/);
  if (/^https?:/i.test(target)) return target;
  if (!pathname) return suffix || null;
  const absolute = path.resolve(root, 'wiki', path.dirname(fromSource), decodeURIComponent(pathname));
  const relative = slash(path.relative(path.join(root, 'wiki'), absolute));
  if (!relative.startsWith('../') && /\.mdx?$/i.test(relative)) {
    if (!known.has(relative)) throw new Error(`wiki/${fromSource}: unknown page ${target}`);
    return `/${pageSlug(relative)}/${suffix}`;
  }
  return github + slash(path.relative(root, absolute)) + suffix;
}

/** Heading anchor as github-slugger makes it for a plain one-word heading. */
const anchor = (heading) => heading.toLowerCase().replace(/[^a-z0-9_ -]/g, '').replace(/ /g, '-');

export async function parseConfigurationReference() {
  const source = 'Reference/Configuration.md';
  const text = renderTemplate(await readFile(path.join(root, 'wiki', source), 'utf8'), source);
  /** @type {Record<string, [string, string | null]>} */
  const docs = {};
  /** @type {Record<string, { anchor: string, options?: string, guide?: string }>} */
  const sections = {};
  const route = `/${pageSlug(source)}/`;
  let section = null;
  let inConfiguration = false;
  for (const line of text.split('\n')) {
    if (/^## /.test(line)) { inConfiguration = line.trim() === '## Configuration'; section = null; continue; }
    if (!inConfiguration) continue;
    const heading = line.match(/^### (.+)$/);
    if (heading) {
      section = heading[1].trim();
      sections[section] = { anchor: `${route}#${anchor(section)}` };
      continue;
    }
    if (!section) continue;
    const links = [...line.matchAll(/\[(\w[^\]]*)\]\(([^)]+)\)/g)];
    if (/^Options: /.test(line) && links[0]) sections[section].options = resolveLink(links[0][2], source);
    const guide = links.find((link) => /details/i.test(link[1]));
    if (/Canonical guide/.test(line) && guide) sections[section].guide = resolveLink(guide[2], source);
    if (!/^\|\s*`/.test(line)) continue;
    const [key, , meaning, canonical] = cells(line);
    const keyName = codeSpans(key)[0];
    if (!keyName || docs[keyName]) continue; // the base table comes first; overrides repeat the meaning
    const link = canonical?.match(/\]\(([^)]+)\)/);
    docs[keyName] = [plain(meaning ?? ''), link ? resolveLink(link[1], source) : null];
  }
  return { docs, sections };
}

/**
 * Module configuration pages that document keys the reference leaves out, in their compact form: keys relative to
 * the section (`Engine`), siblings after a slash (`Query:MaxPageSize` / `MaxWindow`), `<name>` for a dictionary entry,
 * and absolute keys of other sections (`Hangfire:Servers:Search:Enabled`).
 */
export const modulePages = [['Search/Configuration.md', 'Search'], ['Agentic/Configuration.md', 'Agentic']];

export async function parseModulePage(source, section, topLevel) {
  const text = renderTemplate(await readFile(path.join(root, 'wiki', source), 'utf8'), source);
  /** @type {Record<string, [string, string | null]>} */
  const docs = {};
  const guide = `/${pageSlug(source)}/`;
  for (const line of text.split('\n')) {
    if (!/^\|\s*`/.test(line)) continue;
    const [keys, , meaning] = cells(line);
    let previous = null;
    for (const span of codeSpans(keys)) {
      const relative = span.replace(/<[^>]+>/g, '*');
      if (!/^[\w*]+(:[\w*]+)*$/.test(relative)) continue;
      const key = !relative.includes(':') && previous
        ? `${previous.slice(0, previous.lastIndexOf(':') + 1)}${relative}`
        : topLevel.has(relative.split(':')[0]) ? relative : `${section}:${relative}`;
      docs[key] ??= [plain(meaning ?? ''), guide];
      previous = key;
    }
  }
  return docs;
}

/** `| \`jwt_secret\` | \`JWT_SECRET\`, \`JwtSettings:SecretKey\` (...) |` → { 'JwtSettings:SecretKey': 'jwt_secret' }. */
export async function parseSecrets() {
  const source = 'deployment/Secrets.md';
  const text = renderTemplate(await readFile(path.join(root, 'wiki', source), 'utf8'), source);
  /** @type {Record<string, string>} */
  const secrets = {};
  for (const line of text.split('\n')) {
    if (!/^\s*\|\s*`[a-z_<>]+`\s*\|/.test(line)) continue;
    const [name, keys] = cells(line);
    const file = codeSpans(name)[0];
    for (const key of codeSpans(keys ?? '').filter((value) => value.includes(':'))) secrets[key] = file;
  }
  return secrets;
}

/** Top-level `secrets:` entries of every compose file: name → the compose file that declares it. */
export async function parseComposeSecrets() {
  /** @type {Record<string, string>} */
  const declared = {};
  const files = (await readdir(root)).filter((name) => /^docker-compose(\.[\w-]+)?\.ya?ml$/.test(name)).sort();
  for (const file of files) {
    const text = (await readFile(path.join(root, file), 'utf8')).replace(/\r\n?/g, '\n');
    for (const match of text.matchAll(/^ {2}([\w-]+):\n {4}file: \.\/secrets\/\S+/gm)) declared[match[1]] ??= file;
  }
  return declared;
}

/**
 * The configuration keys docker-compose.yml sets on the API through `environment:` (`Key__Sub: ${VAR:-default}`).
 * Environment variables beat appsettings files, so in the Docker target these keys are changed through `.env`.
 */
export async function parseComposeEnvironment() {
  const text = (await readFile(path.join(root, 'docker-compose.yml'), 'utf8')).replace(/\r\n?/g, '\n');
  const start = text.indexOf('environment: &api-environment');
  if (start < 0) throw new Error('docker-compose.yml: the api service has no `environment: &api-environment` block');
  const bindings = [];
  for (const line of text.slice(start).split('\n').slice(1)) {
    if (line.trim() && !/^ {6}/.test(line)) break;
    const match = line.match(/^ {6}([A-Za-z][\w]*(?:__[\w.]+)+):\s*(.+?)\s*$/);
    if (!match) continue;
    const [, name, raw] = match;
    const value = raw.replace(/^"(.*)"$/, '$1');
    const variable = value.match(/^\$\{([A-Z][A-Z0-9_]*)(?::?-(.*))?\}$/);
    bindings.push({ path: name.split('__').join(':'), variable: variable?.[1] ?? null, fallback: variable ? (variable[2] ?? '') : value });
  }
  return bindings;
}

export async function templateSymbols() {
  const template = JSON.parse(await readFile(path.join(root, '.template.config/template.json'), 'utf8'));
  return templateOptions.map(({ symbol, label, modules }) => {
    const definition = template.symbols[symbol];
    if (!definition) throw new Error(`.template.config/template.json: missing symbol ${symbol}`);
    const isChoice = definition.datatype === 'choice';
    return {
      id: symbol, label, modules: modules ?? [], type: isChoice ? 'choice' : 'bool',
      default: isChoice ? definition.defaultValue : definition.defaultValue === 'true',
      description: definition.description ?? '',
      choices: isChoice ? definition.choices.map((choice) => ({ value: choice.choice, description: choice.description ?? '' })) : undefined,
    };
  });
}

export async function buildConfigCatalog() {
  const read = (name) => readFile(path.join(root, 'src/Api', name), 'utf8').then((text) => text.replace(/\r\n?/g, '\n'));
  const [base, development, docker] = await Promise.all(['appsettings.json', 'appsettings.Development.json', 'appsettings.Docker.json'].map(read));
  const env = (await readFile(path.join(root, '.env.example'), 'utf8')).replace(/\r\n?/g, '\n');
  const { docs, sections } = await parseConfigurationReference();
  const topLevel = new Set([...base.matchAll(/^ {2}(?:\/\/)?"([\w]+)":/gm)].map((match) => match[1]));
  for (const [source, section] of modulePages) {
    for (const [key, doc] of Object.entries(await parseModulePage(source, section, topLevel))) docs[key] ??= doc;
    sections[section] ??= { anchor: `/${pageSlug(source)}/`, guide: `/${pageSlug(source)}/` };
  }
  return {
    schemaVersion: 1,
    files: { base, development, docker, env },
    composeEnvironment: await parseComposeEnvironment(),
    symbols: await templateSymbols(),
    docs, sections,
    secrets: await parseSecrets(),
    composeSecrets: await parseComposeSecrets(),
  };
}

export async function writeConfigCatalog() {
  const catalog = await buildConfigCatalog();
  const generated = path.join(app, '.generated');
  await mkdir(generated, { recursive: true });
  await writeFile(path.join(generated, 'config-catalog.json'), JSON.stringify(catalog) + '\n');
  return catalog;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const catalog = await writeConfigCatalog();
  console.log(`Config catalog: ${Object.keys(catalog.docs).length} documented keys, ${Object.keys(catalog.sections).length} sections.`);
}
