import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import { BrandLogo } from '@/components/brand';

export default function NotFound() {
  return <main className="not-found">
    <Link href="/" className="not-found-brand"><BrandLogo /></Link>
    <div className="not-found-cell" aria-hidden="true"><span>404</span><b>?</b><span>No element</span></div>
    <h1>This page is not on the chart</h1>
    <p>The address may be from an older version of the guide, or a page was renamed when the wiki changed.</p>
    <nav aria-label="Ways back">
      <Link href="/">Back to the module chart<ArrowRight size={15} aria-hidden="true" /></Link>
      <Link href="/getting-started/new-project/">Start a new product<ArrowRight size={15} aria-hidden="true" /></Link>
      <Link href="/reference/">Reference<ArrowRight size={15} aria-hidden="true" /></Link>
    </nav>
  </main>;
}
