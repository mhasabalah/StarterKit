import { writeFile } from 'node:fs/promises';
export default async function formatter({ results }) {
  await writeFile('.generated/markdown-findings.json', JSON.stringify(results, null, 2));
}
