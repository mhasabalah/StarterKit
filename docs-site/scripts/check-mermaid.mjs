import { readdir, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer';

const root = new URL('../../', import.meta.url);
const files = ['README.md', ...(await readdir(new URL('wiki/', root), { recursive: true }))
  .filter((name) => name.endsWith('.md')).map((name) => `wiki/${name}`)].sort();
const diagrams = [];
for (const file of files) {
  const lines = (await readFile(new URL(file.replaceAll('\\', '/'), root), 'utf8')).split(/\r?\n/);
  let fence;
  for (let i = 0; i < lines.length; i++) {
    const opening = lines[i].match(/^\s*(`{3,}|~{3,})([^`]*)$/);
    if (!fence && opening) {
      fence = { char: opening[1][0], size: opening[1].length, mermaid: opening[2].trim() === 'mermaid', line: i + 1, source: [] };
    } else if (fence && new RegExp(`^\\s*${fence.char}{${fence.size},}\\s*$`).test(lines[i])) {
      if (fence.mermaid) diagrams.push({ file, line: fence.line, source: fence.source.join('\n') });
      fence = undefined;
    } else if (fence) fence.source.push(lines[i]);
  }
  if (fence?.mermaid) throw new Error(`${file}:${fence.line}: unclosed Mermaid fence`);
}

// Use the same installed browser/parser as mmdc, with no downloads or servers.
const browser = await puppeteer.launch({
  headless: true,
  // Opt in only in restricted runners where Chromium's own sandbox cannot start.
  args: process.env.MERMAID_NO_SANDBOX === '1' ? ['--no-sandbox', '--disable-gpu'] : [],
});
let failures = 0;
try {
  const page = await browser.newPage();
  await page.setRequestInterception(true);
  page.on('request', (request) => request.abort());
  await page.addScriptTag({ path: fileURLToPath(new URL('../node_modules/mermaid/dist/mermaid.js', import.meta.url)) });
  await page.evaluate(() => mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' }));
  for (const diagram of diagrams) {
    try {
      await page.evaluate(async (source) => { await mermaid.parse(source); }, diagram.source);
    } catch (error) {
      failures++;
      console.error(`${diagram.file}:${diagram.line}: ${error.message}`);
    }
  }
} finally { await browser.close(); }
console.log(`Checked ${diagrams.length} Mermaid diagrams: ${failures} failures.`);
process.exitCode = failures ? 1 : 0;
