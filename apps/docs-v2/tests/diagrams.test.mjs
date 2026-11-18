import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { diagramPalettes } from '../lib/mermaid-theme.mjs';
import { discoverDiagrams } from '../scripts/diagram-inventory.mjs';

const luminance = (hex) => hex.match(/[a-f\d]{2}/gi).map((n) => parseInt(n, 16) / 255).map((v) => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
const contrast = (a, b) => { const values = [luminance(a), luminance(b)].sort((a, b) => b - a); return (values[0] + .05) / (values[1] + .05); };

test('diagram text, semantic boundaries and edges contrast in both themes', () => {
  for (const [theme, palette] of Object.entries(diagramPalettes)) {
    assert.ok(contrast(palette.text, palette.surface) >= 4.5, `${theme}: text`);
    assert.ok(contrast(palette.edge, palette.background) >= 3, `${theme}: edge`);
    assert.ok(contrast(palette.border, palette.surface) >= 3, `${theme}: group boundary`);
    for (const [role, [fill, border]] of Object.entries(palette.roles)) {
      assert.ok(contrast(palette.text, fill) >= 4.5, `${theme}: ${role} text`);
      assert.ok(contrast(border, fill) >= 3, `${theme}: ${role} boundary`);
    }
  }
});

test('every canonical diagram has a reviewed source and accessible alternative', async () => {
  const diagrams = await discoverDiagrams();
  const review = JSON.parse(await readFile('quality/diagram-review.json', 'utf8'));
  assert.equal(diagrams.length, review.diagrams.length);
  for (const diagram of diagrams) {
    const entry = review.diagrams.find((entry) => entry.file === diagram.source && entry.diagram === diagram.number);
    assert.ok(entry, `${diagram.source} #${diagram.number}`);
    assert.equal(entry.status, 'reviewed');
    assert.ok(entry.evidence.length > 0, 'Record the source evidence used in review');
    for (const file of entry.evidence) await access(`../../${file}`);
    assert.equal(createHash('sha256').update(diagram.code).digest('hex'), entry.after, `Update review after changing ${diagram.source} #${diagram.number}`);
    assert.match(diagram.code, /accTitle: .+/);
    assert.match(diagram.code, /accDescr: .+/);
    assert.doesNotMatch(diagram.code, /(?:classDef|%%\{init|^\s*style\s)/m);
  }
});
