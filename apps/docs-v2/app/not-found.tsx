import Link from 'next/link';
export default function NotFound() { return <main className="not-found"><p className="page-eyebrow">404</p><h1>Page not found</h1><p>This documentation page does not exist.</p><Link href="/">Return to documentation</Link></main>; }
