import { chromium } from '@playwright/test';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { once } from 'node:events';
import path from 'node:path';
import { mkdir, writeFile } from 'node:fs/promises';
import { discoverDiagrams } from './diagram-inventory.mjs';

const root = path.resolve('node_modules/mermaid/dist');
const server = createServer(async (request, response) => {
  if (request.url === '/theme.mjs') { response.setHeader('Content-Type', 'text/javascript'); response.end(await readFile('lib/mermaid-theme.mjs')); return; }
  const font = request.url.match(/^\/fonts\/(400|600|700)\.woff2$/);
  if (font) { response.setHeader('Content-Type', 'font/woff2'); response.end(await readFile(`node_modules/@fontsource/ibm-plex-sans/files/ibm-plex-sans-latin-${font[1]}-normal.woff2`)); return; }
  if (request.url === '/') { response.setHeader('Content-Type', 'text/html'); response.end('<!doctype html><html><body></body></html>'); return; }
  const file = path.resolve(root, '.' + new URL(request.url, 'http://localhost').pathname);
  if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
  try { response.setHeader('Content-Type', 'text/javascript'); response.end(await readFile(file)); }
  catch { response.writeHead(404).end(); }
});
server.listen(0, 'localhost'); await once(server, 'listening');
const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHANNEL });
try {
  const page = await browser.newPage();
  await page.goto(`http://localhost:${server.address().port}`);
  await page.evaluate(async () => {
    for (const weight of [400, 600, 700]) {
      const font = await new FontFace('IBM Plex Sans', `url(/fonts/${weight}.woff2)`, { weight: String(weight) }).load();
      document.fonts.add(font);
    }
    await document.fonts.ready;
  });
  await page.evaluate(async () => { const { default: mermaid } = await import('/mermaid.esm.min.mjs'); window.mermaid = mermaid; window.diagramTheme = (await import('/theme.mjs')).diagramTheme; });
  const diagrams = await discoverDiagrams();
  const fixtures = [
    'flowchart TD\n A[Client] --> B[Server]',
    'sequenceDiagram\n Client->>Server: Request\n Server-->>Client: Response',
    'classDiagram\n class Example {\n +string Name\n }',
    'erDiagram\n TENANT ||--o{ ITEM : owns',
    'stateDiagram-v2\n [*] --> Ready\n Ready --> [*]',
    'journey\n title Documentation\n section Learn\n Read guide: 5: Developer',
    'gantt\n title Example\n dateFormat YYYY-MM-DD\n section Plan\n Review: 2026-01-01, 1d',
    'C4Context\n Person(reader, "Reader")\n System(docs, "Documentation")\n Rel(reader, docs, "Reads")',
  ];
  for (const [i, fixture] of fixtures.entries()) diagrams.push({ source: `fixture-${i}`, line: 1, code: fixture });
  const results = [];
  await mkdir('.generated/diagram-previews', { recursive: true });
  for (const dark of [false, true]) {
    for (const [i, diagram] of diagrams.entries()) {
      try {
        const rendered = await page.evaluate(async ({ code, id, dark }) => {
          window.mermaid.initialize(window.diagramTheme(dark));
          await window.mermaid.parse(code);
          const { svg } = await window.mermaid.render(id, code);
          const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');
          const root = doc.documentElement;
          return { svg, viewBox: root.getAttribute('viewBox'), title: root.querySelector('title')?.textContent, description: root.querySelector('desc')?.textContent };
        }, { code: diagram.code, id: `check-${dark ? 'dark' : 'light'}-${i}`, dark });
        if (!diagram.source.startsWith('fixture-') && (!rendered.title || !rendered.description)) throw new Error('Missing accessible SVG title or description');
        if (!diagram.source.startsWith('fixture-') && Number(rendered.viewBox?.split(' ')[2]) > 860) throw new Error(`Diagram exceeds the 860px width budget: ${rendered.viewBox}`);
        results.push({ source: diagram.source, line: diagram.line, theme: dark ? 'dark' : 'light', viewBox: rendered.viewBox, title: rendered.title });
        if (!diagram.source.startsWith('fixture-')) await writeFile(`.generated/diagram-previews/${dark ? 'dark' : 'light'}-${i}.svg`, rendered.svg);
      } catch (error) { throw new Error(`${diagram.source}:${diagram.line}: ${error.message}`); }
    }
  }
  await writeFile('.generated/diagram-rendering.json', JSON.stringify(results, null, 2));
  console.log(`Parsed and rendered ${diagrams.length} diagrams in BOTH themes, including all eight required diagram families.`);
} finally { await browser.close(); server.close(); }
