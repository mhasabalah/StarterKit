// The architecture model (ADR-0035): wiki/Architecture/model/*.c4 is validated, compiled to the React views module
// and summarised for tests, the remark transform and the Markdown served to agents. The model is only rebuilt when
// its sources change, so the dev watcher stays fast.
import { execFileSync } from 'node:child_process';
import { readdir, readFile, writeFile, mkdir, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
export { outline } from '../lib/architecture-outline.mjs';

const app = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(app, '../..');
export const modelDir = path.join(root, 'wiki/Architecture/model');
export const modelRepoPath = 'wiki/Architecture/model';
const out = path.join(app, '.generated/likec4');
const summaryFile = path.join(app, '.generated/architecture-views.json');
const cli = path.join(app, 'node_modules/likec4/bin/likec4.mjs');
const strip = (text) => String(text ?? '').replace(/\x1b\[[0-9;]*m/g, '');

function run(...args) {
  try {
    return execFileSync(process.execPath, [cli, ...args], { cwd: app, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (error) {
    const log = strip(`${error.stdout}\n${error.stderr}`).split('\n').filter((line) => !/\bINFO\b/.test(line)).join('\n').trim();
    throw new Error(`likec4 ${args[0]} failed for ${modelRepoPath}:\n${log}`);
  }
}

/** Model source files, in a stable order (the template removes optional module files from products). */
export async function modelSources() {
  const names = (await readdir(modelDir)).filter((name) => name.endsWith('.c4') || name === 'likec4.config.json' || name === 'likec4.css').sort();
  return Promise.all(names.map(async (name) => ({ name, path: `${modelRepoPath}/${name}`, text: (await readFile(path.join(modelDir, name), 'utf8')).replace(/\r\n?/g, '\n') })));
}

const text = (value) => (typeof value === 'string' ? value : value?.txt ?? value?.md ?? '').trim();

/** Validates and compiles the model; returns the view summaries. */
export async function buildArchitecture() {
  if (!existsSync(modelDir)) return [];
  const sources = await modelSources();
  const key = createHash('sha256').update(JSON.stringify([sources, JSON.parse(await readFile(path.join(app, 'node_modules/likec4/package.json'), 'utf8')).version])).digest('hex');
  const stamp = path.join(out, 'source.sha256');
  if (existsSync(summaryFile) && existsSync(path.join(out, 'views.mjs')) && existsSync(stamp) && (await readFile(stamp, 'utf8')) === key) {
    return JSON.parse(await readFile(summaryFile, 'utf8')).views;
  }
  await rm(out, { recursive: true, force: true });
  await mkdir(out, { recursive: true });
  run('validate', modelDir);
  run('gen', 'react', '--no-app-config', '-o', path.join(out, 'views.mjs'), modelDir);
  const exported = path.join(out, 'model.json');
  run('export', 'json', '-o', exported, modelDir);
  const projects = JSON.parse(await readFile(exported, 'utf8'));
  await rm(exported);
  const project = (Array.isArray(projects) ? projects : [projects])[0];
  const views = Object.values(project.views).map((view) => ({
    id: view.id,
    type: view._type,
    title: text(view.title),
    description: text(view.description),
    source: view.sourcePath ? `${modelRepoPath}/${path.basename(view.sourcePath)}` : undefined,
    width: Math.round(view.bounds.width),
    height: Math.round(view.bounds.height),
    nodes: view.nodes.map((node) => ({ id: node.id, title: text(node.title), kind: node.kind, technology: node.technology || undefined })),
    edges: view.edges.map((edge) => ({ source: edge.source, target: edge.target, label: text(edge.label), technology: edge.technology || undefined })),
  })).sort((a, b) => a.id.localeCompare(b.id));
  await writeFile(summaryFile, JSON.stringify({ project: project.projectId, views }, null, 2) + '\n');
  await writeFile(stamp, key);
  return views;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const views = await buildArchitecture();
  console.log(`Architecture model: ${views.length} views.`);
}
