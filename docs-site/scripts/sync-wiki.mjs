import { readdir, readFile, writeFile, mkdir, rm, copyFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { pages, outputPath, pageSlug, siteBase, repository } from '../sidebar.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const wiki = path.join(root, 'wiki');
const output = path.join(root, 'docs-site/src/content/docs');
const github = `https://github.com/${repository}`;
const slash = (value) => value.split(path.sep).join('/');
const encodePath = (value) => value.split('/').map(encodeURIComponent).join('/');

async function walk(directory, prefix = '') {
  const entries = await readdir(directory, { withFileTypes: true });
  const result = [];
  for (const entry of entries.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
    const relative = prefix + entry.name;
    if (entry.isSymbolicLink()) throw new Error(`Symlink is not supported: ${relative}`);
    if (entry.isDirectory()) result.push(...await walk(path.join(directory, entry.name), relative + '/'));
    else result.push(relative);
  }
  return result;
}

// Recursive descent, never eval: every identifier is true in the full-template view.
export function evaluate(expression) {
  const tokens = expression.match(/&&|\|\||[!()]|[A-Za-z_][A-Za-z0-9_]*|\S/g) || [];
  let position = 0;
  function atom() {
    const token = tokens[position++];
    if (token === '!') return !atom();
    if (token === '(') {
      const value = or();
      if (tokens[position++] !== ')') throw new Error('Expected closing parenthesis');
      return value;
    }
    if (token && /^[A-Za-z_][A-Za-z0-9_]*$/.test(token)) return true;
    throw new Error(`Unexpected expression token: ${token ?? 'end of expression'}`);
  }
  function and() {
    let value = atom();
    while (tokens[position] === '&&') { position++; const right = atom(); value = value && right; }
    return value;
  }
  function or() {
    let value = and();
    while (tokens[position] === '||') { position++; const right = and(); value = value || right; }
    return value;
  }
  const value = or();
  if (position !== tokens.length) throw new Error(`Unexpected expression token: ${tokens[position]}`);
  return value;
}

export function renderTemplate(text, source) {
  const stack = [];
  const result = [];
  for (const [index, line] of text.replace(/\r\n?/g, '\n').split('\n').entries()) {
    const active = () => stack.every((frame) => frame.active);
    if (!/^\s*<!--#/.test(line)) { if (active()) result.push(line); continue; }
    try {
      const match = line.match(/^\s*<!--#(if|elseif|else|endif)\s*(.*?)-->\s*$/);
      if (!match) throw new Error('Invalid template marker');
      const [, kind, expression] = match;
      if (kind === 'if' || kind === 'elseif') {
        const take = evaluate(expression);
        if (kind === 'if') stack.push({ active: take, taken: take, elseSeen: false });
        else {
          const frame = stack.at(-1);
          if (!frame || frame.elseSeen) throw new Error('Unexpected elseif');
          frame.active = !frame.taken && take;
          frame.taken ||= take;
        }
      } else {
        if (expression) throw new Error(`Unexpected expression after ${kind}`);
        const frame = stack.at(-1);
        if (!frame) throw new Error(`Unexpected ${kind}`);
        if (kind === 'endif') stack.pop();
        else {
          if (frame.elseSeen) throw new Error('Duplicate else');
          frame.active = !frame.taken;
          frame.taken = frame.elseSeen = true;
        }
      }
    } catch (error) { throw new Error(`${source}:${index + 1}: ${error.message}`); }
  }
  if (stack.length) throw new Error(`${source}:${text.split('\n').length}: Unclosed template conditional`);
  // Inline examples are documentation, not directives. Keep them readable without literal markers.
  return result.join('\n').replace(/`([^`\n]*<!--#[^`\n]*)`/g, (_, code) =>
    `<code>${code.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')}</code>`);
}

function plain(text) {
  return text.replace(/!?\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/<[^>]+>/g, '')
    .replace(/[`*_~]/g, '').replace(/\s+/g, ' ').trim();
}

async function rewriteTarget(target, source) {
  if (/^(?:[a-z][a-z\d+.-]*:|#|\/\/)/i.test(target)) return target;
  const [, pathname, suffix = ''] = target.match(/^([^?#]*)(.*)$/);
  if (!pathname) return target;
  const absolute = path.resolve(path.dirname(path.join(wiki, source)), decodeURIComponent(pathname));
  const relative = slash(path.relative(wiki, absolute));
  if (relative !== '..' && !relative.startsWith('../') && !path.isAbsolute(relative)) {
    if (relative === 'INDEX.md') return siteBase + suffix;
    if (/\.md$/i.test(relative)) return siteBase + pageSlug(relative) + '/' + suffix;
    return siteBase + encodePath(relative.toLowerCase()) + suffix;
  }
  const repoPath = slash(path.relative(root, absolute));
  if (repoPath.startsWith('../')) throw new Error(`${source}: Link escapes repository: ${target}`);
  const isDirectory = await stat(absolute).then((s) => s.isDirectory()).catch(() => pathname.endsWith('/'));
  return `${github}/${isDirectory ? 'tree' : 'blob'}/master/${encodePath(repoPath)}${suffix}`;
}

async function rewriteLinks(text, source) {
  const lines = text.split('\n');
  let fence;
  for (let i = 0; i < lines.length; i++) {
    const marker = lines[i].match(/^ {0,3}(`{3,}|~{3,})/);
    if (marker) {
      if (!fence) fence = marker[1];
      else if (marker[1][0] === fence[0] && marker[1].length >= fence.length && /^\s*$/.test(lines[i].slice(marker[0].length))) fence = undefined;
      continue;
    }
    if (fence) continue;
    // Skip code spans; recognize balanced parentheses and optional titles in destinations.
    const line = lines[i];
    let rewritten = '', cursor = 0;
    const pattern = /(`+)[\s\S]*?\1|\]\(\s*(<[^>]*>|(?:[^\s()]+|\([^()]*\))+)(?=\s|\))|^ {0,3}\[[^\]]+\]:\s*(<[^>]*>|\S+)/g;
    for (const match of line.matchAll(pattern)) {
      if (match[1]) continue;
      const raw = match[2] ?? match[3];
      const offset = match.index + match[0].lastIndexOf(raw);
      const angle = raw.startsWith('<');
      const target = angle ? raw.slice(1, -1) : raw;
      const replacement = await rewriteTarget(target, source);
      rewritten += line.slice(cursor, offset) + (angle ? `<${replacement}>` : replacement);
      cursor = offset + raw.length;
    }
    lines[i] = rewritten + line.slice(cursor);
  }
  return lines.join('\n');
}

async function main() {
  const files = await walk(wiki);
  const markdown = files.filter((file) => /\.md$/i.test(file) && file !== 'INDEX.md');
  const inventory = new Map();
  const destinations = new Set(['index.mdx']);
  for (const page of pages) {
    if (inventory.has(page.source)) throw new Error(`Duplicate sidebar page: ${page.source}`);
    if (!markdown.includes(page.source)) throw new Error(`Sidebar page does not exist: ${page.source}`);
    const destination = outputPath(page.source);
    if (destinations.has(destination)) throw new Error(`Output collision: ${page.source}`);
    destinations.add(destination);
    inventory.set(page.source, page);
  }
  const missing = markdown.filter((file) => !inventory.has(file));
  if (missing.length) throw new Error(`Wiki pages missing from sidebar.mjs:\n${missing.join('\n')}`);

  // Validate and render before replacing the generated tree. Preserve only the committed splash.
  const rendered = [];
  for (const source of markdown) {
    const lines = renderTemplate(await readFile(path.join(wiki, source), 'utf8'), source).split('\n');
    let fence = false;
    const heading = lines.findIndex((line) => {
      if (/^\s*(`{3,}|~{3,})/.test(line)) fence = !fence;
      return !fence && /^# /.test(line);
    });
    if (heading < 0) throw new Error(`${source}: Missing H1`);
    const title = plain(lines[heading].slice(2));
    const paragraphs = lines.slice(heading + 1).join('\n').split(/\n\s*\n/);
    const paragraph = paragraphs.find((p) => p.trim() && !/^[\s]*([#|>`~<]|[-+*]\s|\d+\.\s|!\[)/.test(p) && !p.includes('\n|'));
    const description = paragraph ? plain(paragraph).slice(0, 160) : '';
    lines.splice(heading, 1);
    const page = inventory.get(source);
    const frontmatter = [`title: ${JSON.stringify(title)}`,
      ...(description ? [`description: ${JSON.stringify(description)}`] : []),
      `editUrl: ${JSON.stringify(`${github}/edit/master/wiki/${encodePath(source)}`)}`,
      'sidebar:', `  label: ${JSON.stringify(page.label)}`, `  order: ${page.order}`];
    // Generated files have no Git history; inherit the source's last committed date.
    const updated = execFileSync('git', ['log', '-1', '--format=%cI', '--', `wiki/${source}`], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    if (updated) frontmatter.push(`lastUpdated: ${updated}`);
    rendered.push([outputPath(source), `---\n${frontmatter.join('\n')}\n---\n${await rewriteLinks(lines.join('\n'), source)}`]);
  }
  await mkdir(output, { recursive: true });
  for (const entry of await readdir(output)) {
    if (entry === 'index.mdx') continue;
    const target = path.resolve(output, entry);
    if (!target.startsWith(output + path.sep)) throw new Error('Unsafe output path');
    await rm(target, { recursive: true, force: true });
  }
  for (const [destination, text] of rendered) {
    await mkdir(path.dirname(path.join(output, destination)), { recursive: true });
    await writeFile(path.join(output, destination), text);
  }
  for (const asset of files.filter((file) => !/\.md$/i.test(file))) {
    const destination = path.join(output, asset.toLowerCase());
    await mkdir(path.dirname(destination), { recursive: true });
    await copyFile(path.join(wiki, asset), destination);
  }
  console.log(`Synced ${rendered.length} wiki pages; preserved index.mdx; skipped wiki/INDEX.md.`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => { console.error(`sync-wiki: ${error.message}`); process.exitCode = 1; });
}
