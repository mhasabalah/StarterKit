import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
export async function brandTokens() {
  const css = await readFile(new URL('../src/styles/brand.css', import.meta.url), 'utf8');
  return Object.fromEntries(['dark', 'light'].map(theme => {
    const selector = theme === 'dark' ? /:root\s*\{([^}]+)\}/ : /:root\[data-theme='light'\]\s*\{([^}]+)\}/;
    const block = css.match(selector)?.[1];
    assert(block, 'Missing '+theme+' tokens');
    return [theme, Object.fromEntries([...block.matchAll(/--brand-([\w-]+):\s*(#[\da-f]{6});/gi)].map(m => [m[1], m[2].toLowerCase()]))];
  }));
}
function luminance(hex) {
  assert.match(hex ?? '', /^#[\da-f]{6}$/i, 'Expected a concrete six-digit token');
  const rgb = hex.slice(1).match(/../g).map(c => parseInt(c, 16) / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4);
  return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const results = [];
  for (const [theme, tokens] of Object.entries(await brandTokens())) {
    const pairs = ['bg', 'surface'].flatMap(bg => ['text', 'muted', 'primary', 'teal'].map(fg => [fg, bg]));
    pairs.push(['on-primary', 'primary'], ['primary-high', 'primary-low'], ['text', 'primary-low']);
    for (const kind of ['note', 'tip', 'caution', 'danger']) pairs.push([kind, kind+'-bg']);
    for (const [fg, bg] of pairs) {
      const a = luminance(tokens[fg]), b = luminance(tokens[bg]);
      const ratio = (Math.max(a, b) + .05) / (Math.min(a, b) + .05);
      results.push({ theme, pair: fg+' / '+bg, ratio: ratio.toFixed(2), AA: ratio >= 4.5 ? 'PASS' : 'FAIL' });
    }
  }
  console.table(results);
  assert(results.every(r => r.AA === 'PASS'), 'WCAG AA requires at least 4.5:1');
}
