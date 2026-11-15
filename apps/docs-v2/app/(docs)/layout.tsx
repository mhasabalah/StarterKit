import { DocsLayout } from '@/components/docs-layout';
import { baseOptions } from '@/lib/layout';
import { pageTree } from '@/lib/source';

export default function Layout({ children }: { children: React.ReactNode }) {
  return <DocsLayout {...baseOptions} tree={pageTree}>{children}</DocsLayout>;
}
