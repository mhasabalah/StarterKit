// Refreshes the `architecture` section of quality/diagram-review.json after a reviewed change to the model or the
// hero: run it, then commit the new hashes with the change. It only re-hashes files that already have a review
// record or are new (new files get status "pending" until a person reviews them and sets "reviewed").
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { modelSources } from './architecture.mjs';

const app = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(app, '../..');
export const heroPath = 'wiki/Architecture/diagrams/system-big-picture.excalidraw.svg';
export const sha256 = (text) => createHash('sha256').update(text.replace(/\r\n?/g, '\n')).digest('hex');

export async function architectureFiles() {
  const model = (await modelSources()).map((file) => ({ file: file.path, kind: 'likec4', after: sha256(file.text) }));
  const hero = { file: heroPath, kind: 'excalidraw', after: sha256(await readFile(path.join(root, heroPath), 'utf8')) };
  return [...model, hero];
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const reviewFile = path.join(app, 'quality/diagram-review.json');
  const review = JSON.parse(await readFile(reviewFile, 'utf8'));
  const previous = new Map((review.architecture ?? []).map((entry) => [entry.file, entry]));
  review.architecture = (await architectureFiles()).map((file) => ({ ...(previous.get(file.file) ?? { review: '', evidence: [], status: 'pending' }), ...file }));
  await writeFile(reviewFile, JSON.stringify(review, null, 2) + '\n');
  console.log(`architecture review: ${review.architecture.length} files`);
}
