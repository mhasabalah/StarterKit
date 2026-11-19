import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('module chart orders mirror ModuleOrder.cs', async () => {
  const chart = await readFile('lib/chart.ts', 'utf8');
  const source = await readFile('../../src/Shared/Shared.Core/Modularity/ModuleOrder.cs', 'utf8');
  const constants = Object.fromEntries([...source.matchAll(/const int (\w+) = (\d+);/g)].map(([, name, value]) => [name, Number(value)]));
  const tiles = [...chart.matchAll(/name: '(\w+)', family: '\w+', order: (\d+)/g)].map(([, name, order]) => [name, Number(order)]);
  assert.ok(tiles.length >= 10);
  for (const [name, order] of tiles) {
    // Shipping is the sample of a new module and sits in the Default slot.
    const expected = name === 'Shipping' ? constants.Default : constants[name];
    assert.equal(order, expected, `${name} should be ${expected}`);
  }
});
