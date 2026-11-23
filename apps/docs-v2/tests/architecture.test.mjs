import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { modelSources } from '../scripts/architecture.mjs';
import { architectureFiles, heroPath } from '../scripts/review-architecture.mjs';

// The architecture model (ADR-0035): wiki/Architecture/model/*.c4, embedded in pages with ```likec4-view fences.
const root = fileURLToPath(new URL('../../../', import.meta.url));
const summary = JSON.parse(await readFile(new URL('../.generated/architecture-views.json', import.meta.url), 'utf8'));
const views = new Map(summary.views.map((view) => [view.id, view]));
const wikiPages = execFileSync('git', ['ls-files', '-c', '-o', '--exclude-standard', 'wiki'], { cwd: root, encoding: 'utf8' })
  .split('\n').filter((file) => file.endsWith('.md'));
const fences = [];
for (const file of wikiPages) {
  const lines = (await readFile(path.join(root, file), 'utf8')).replace(/\r\n?/g, '\n').split('\n');
  lines.forEach((line, index) => {
    if (line.trim() === '```likec4-view') fences.push({ file, line: index + 1, id: lines[index + 1].trim(), after: lines.slice(index + 3, index + 6) });
  });
}

const luminance = (hex) => hex.match(/[a-f\d]{2}/gi).map((n) => parseInt(n, 16) / 255).map((v) => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
const contrast = (a, b) => { const values = [luminance(a), luminance(b)].sort((x, y) => y - x); return (values[0] + .05) / (values[1] + .05); };

test('every architecture view has an accessible title and a one-sentence description', () => {
  assert.ok(views.size > 0, 'run npm run sync first');
  for (const view of views.values()) {
    assert.ok(view.title.length > 3, `${view.id}: title`);
    assert.match(view.description, /^[A-Z].{30,}\.$/s, `${view.id}: description is one sentence ending in a full stop`);
  }
});

test('pages embed only defined views, with a caption, and every view is used', () => {
  for (const fence of fences) {
    assert.ok(views.has(fence.id), `${fence.file}:${fence.line} unknown view ${fence.id}`);
    assert.ok(fence.after.some((line) => /^\*Figure: .+\.\*$/.test(line)), `${fence.file}:${fence.line} needs a *Figure: …* caption`);
  }
  const used = new Set(fences.map((fence) => fence.id));
  for (const id of views.keys()) assert.ok(used.has(id), `view ${id} is not embedded in any page`);
});

test('element colours keep white labels readable', async () => {
  const specification = (await modelSources()).find((file) => file.name === 'specification.c4').text;
  const colours = [...specification.matchAll(/^\s*color\s+(role-[a-z]+)\s+(#[0-9a-f]{6})/gim)];
  assert.ok(colours.length >= 7, 'one colour per documentation role');
  for (const [, name, hex] of colours) assert.ok(contrast('#ffffff', hex) >= 4.5, `${name} ${hex} against white text`);
});

test('an optional module is mentioned only in its own model file and pages', async () => {
  const optional = { catalog: ['catalog.c4'], shipping: ['shipping.c4'], agentic: ['agentic.c4', 'shipping.c4'] };
  for (const file of await modelSources()) {
    for (const [name, owners] of Object.entries(optional)) {
      if (owners.includes(file.name)) continue;
      assert.doesNotMatch(file.text, new RegExp(`starterkit\\.app\\.${name}\\b`), `${file.name} references ${name}; move the edge to ${owners[0]}`);
    }
  }
  const optionalPages = { catalog: ['wiki/Modules/Catalog.md'], shipping: ['wiki/Modules/Shipping.md'], agentic: ['wiki/Modules/Agentic.md', 'wiki/Modules/Shipping.md'] };
  for (const fence of fences) {
    const view = views.get(fence.id);
    const optionalModule = Object.keys(optional).find((name) => view.source?.endsWith(`/${name}.c4`));
    if (optionalModule) assert.ok(optionalPages[optionalModule].includes(fence.file), `${fence.file} embeds ${fence.id} from optional ${optionalModule}`);
  }
});

test('every model file and the hero have a current review record', async () => {
  const review = JSON.parse(await readFile(new URL('../quality/diagram-review.json', import.meta.url), 'utf8'));
  const files = await architectureFiles();
  assert.deepEqual((review.architecture ?? []).map((entry) => entry.file).sort(), files.map((file) => file.file).sort(), 'run node scripts/review-architecture.mjs');
  for (const file of files) {
    const entry = review.architecture.find((candidate) => candidate.file === file.file);
    assert.equal(entry.status, 'reviewed', `${file.file}: review it, then set status reviewed`);
    assert.equal(entry.after, file.after, `${file.file} changed: review it and run node scripts/review-architecture.mjs`);
    assert.ok(entry.review && entry.evidence.length > 0, `${file.file}: record the review and its source evidence`);
    for (const evidence of entry.evidence) await access(path.join(root, evidence));
  }
});

test('the hero keeps its embedded Excalidraw scene', async () => {
  const svg = await readFile(path.join(root, heroPath), 'utf8');
  assert.match(svg, /payload-type:application\/vnd\.excalidraw\+json/, 'save the hero with the scene embedded');
  assert.match(svg, /<svg[^>]+viewBox=/);
});
