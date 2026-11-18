import { main } from 'markdownlint-cli2';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
await main({ optionsOverride: { outputFormatters: [['./scripts/markdown-formatter.mjs']] } });
const results = JSON.parse(await readFile('.generated/markdown-findings.json', 'utf8'));
const findings = [];
for (const result of results) {
  const line = (await readFile(result.fileName, 'utf8')).split('\n')[result.lineNumber - 1];
  findings.push({ file: result.fileName.replaceAll('\\', '/'), rule: result.ruleNames[0], lineHash: createHash('sha256').update(line).digest('hex'), context: line.trim(), reason: 'Inherited canonical wiki formatting; preserved to avoid unrelated V1 content edits.' });
}
const baselinePath = 'quality/markdown-baseline.json';
if (process.argv.includes('--record-baseline')) {
  if (process.env.CI) throw new Error('Baseline updates are forbidden in CI');
  await writeFile(baselinePath, JSON.stringify(findings, null, 2) + '\n');
  console.log(`Recorded ${findings.length} inherited findings for review.`);
} else {
  const baseline = JSON.parse(await readFile(baselinePath, 'utf8'));
  const key = (issue) => `${issue.file}:${issue.rule}:${issue.lineHash}`;
  const counts = new Map();
  for (const issue of baseline) counts.set(key(issue), (counts.get(key(issue)) || 0) + 1);
  const unexpected = findings.filter((issue) => { const count = counts.get(key(issue)) || 0; if (!count) return true; counts.set(key(issue), count - 1); return false; });
  if (unexpected.length) throw new Error(`New Markdown violations:\n${unexpected.map((issue) => `${issue.file}: ${issue.rule} ${issue.context}`).join('\n')}`);
  console.log(`Markdown checks passed; ${findings.length} explicit inherited formatting exceptions.`);
}
