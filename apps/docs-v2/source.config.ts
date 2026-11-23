import { defineDocs, defineConfig } from 'fumadocs-mdx/config';
import { pageSchema } from 'fumadocs-core/source/schema';
import { remarkCallouts } from './lib/remark-callouts';
import { remarkDirectiveAdmonition } from 'fumadocs-core/mdx-plugins/remark-directive-admonition';
import { z } from 'zod';
import { remarkMermaid } from './lib/remark-mermaid';
import { remarkLikeC4 } from './lib/remark-likec4';

export const docs = defineDocs({
  dir: 'content/docs',
  docs: {
    schema: pageSchema.extend({ source: z.string(), lastUpdated: z.string().optional() }),
    postprocess: { includeProcessedMarkdown: true },
  },
});
// Restrict directive parsing to containers before transforming callouts; prose colons remain text.
export default defineConfig({ mdxOptions: { remarkPlugins: [remarkCallouts, remarkDirectiveAdmonition, remarkMermaid, remarkLikeC4] } });
