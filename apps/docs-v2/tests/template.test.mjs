import test from 'node:test';
import assert from 'node:assert/strict';
import { renderTemplate, evaluate } from '../scripts/template.mjs';

test('full template selects active branches and nested conditionals', () => {
  assert.equal(evaluate('sample && (tenancy || !aiTooling)'), true);
  assert.equal(renderTemplate('<!--#if (sample)-->\nyes\n<!--#else-->\nno\n<!--#endif-->', 'example'), 'yes');
  assert.equal(renderTemplate('<!--#if (!sample)-->\nno\n<!--#elseif (tenancy)-->\nyes\n<!--#endif-->', 'example'), 'yes');
});
test('invalid template directives fail with source context', () => {
  assert.throws(() => renderTemplate('<!--#if (sample)-->\ntext', 'broken.md'), /broken.md.*Unclosed/);
  assert.throws(() => evaluate('sample; process.exit()'), /Unexpected/);
});
