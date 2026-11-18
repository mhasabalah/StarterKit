import { execFileSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { unified } from 'unified';
import remarkParse from 'remark-parse';
import { visit } from 'unist-util-visit';

const root = fileURLToPath(new URL('../../../', import.meta.url));
export async function discoverDiagrams() {
  const files = [...new Set(execFileSync('git', ['ls-files', '-c', '-o', '--exclude-standard'], { cwd: root, encoding: 'utf8' }).split('\n'))]
    .filter((file) => /\.mdx?$/.test(file) && !/^(?:docs-site\/src\/content\/docs|apps\/docs-v2\/content\/docs)\//.test(file));
  const diagrams = [];
  for (const file of files.sort()) {
    let ordinal = 0;
    const raw = await readFile(path.join(root, file), 'utf8');
    const tree = unified().use(remarkParse).parse(raw.replace(/\r\n?/g, '\n'));
    visit(tree, 'code', (node) => {
      if (node.lang !== 'mermaid') return;
      diagrams.push({ source: file, number: ++ordinal, line: node.position.start.line, code: node.value });
    });
  }
  return diagrams;
}
