import type { Processor } from 'unified';
import type {} from 'remark-parse';
import remarkDirective from 'remark-directive';

// Only ::: containers are callouts. Colons in prose (03:00, URLs, identifiers) stay literal.
export function remarkCallouts(this: Processor) {
  remarkDirective.call(this);
  const extension = this.data().micromarkExtensions?.at(-1);
  if (extension) delete extension.text;
  const flow = extension?.flow?.[58];
  if (Array.isArray(flow)) extension!.flow![58] = flow.slice(0, 1);
}
