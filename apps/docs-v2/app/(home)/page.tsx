import Link from 'next/link';
import { ArrowRight, ArrowUpRight, Terminal } from 'lucide-react';
import { ServerCodeBlock } from 'fumadocs-ui/components/codeblock.rsc';
import { BrandLogo } from '@/components/brand';
import { ModuleChart, ContinueReading } from '@/components/module-chart';
import { SearchTrigger } from '@/components/search-trigger';
import { families, modules, practices } from '@/lib/chart';
import { source } from '@/lib/source';
import { absoluteUrl, repository } from '@/lib/site';
import type { Metadata } from 'next';

export const metadata: Metadata = {
  description: 'Build, extend and operate a StarterKit product. Guides to ASP.NET Core modules, tenant isolation, messaging and deployment.',
  alternates: { canonical: absoluteUrl('/') },
};

const routes = [
  { title: 'Start a new product', description: 'Generate the solution, choose the defaults and run the stack locally with Aspire.', url: '/getting-started/new-project/', action: 'Setup guide' },
  { title: 'Understand the architecture', description: 'Follow a request through the host, the module boundaries and the contracts between them.', url: '/architecture/overview/', action: 'Architecture overview' },
  { title: 'Build your first module', description: 'Scaffold a module with dotnet new starterkit-module and keep to the conventions the tests enforce.', url: '/getting-started/new-project/#6-build-your-first-module', action: 'First module' },
];

/** Real guardrails from src/Api.Tests/Architecture (see /reference/architecture-guardrails/). */
const guardrails = [
  { test: 'ModuleBoundaryTests', fails: 'A module references another module’s implementation instead of its contracts.' },
  { test: 'TenantIsolationArchitectureTests', fails: 'A tenant entity lacks IMustHaveTenant, or a unique index does not start with tenant_id.' },
  { test: 'MessagingArchitectureTests', fails: 'An integration event lives outside .Contracts or reuses a message URN.' },
  { test: 'AdminEndpointAuthorizationTests', fails: 'Any endpoint declares no authorization at all.' },
  { test: 'PendingModelChangesTests', fails: 'The EF model changed without a migration.' },
];

/** Architecture decision records, newest first, with the decision itself as the title. */
const decisions = source.getPages()
  .map((page) => ({ url: page.url, number: page.url.match(/^\/decisions\/(\d{4})-/)?.[1], title: String(page.data.title).replace(/^\d{4}\.\s*/, '') }))
  .filter((page): page is { url: string; number: string; title: string } => !!page.number && page.number !== '0000')
  .sort((a, b) => b.number.localeCompare(a.number));

export default function Home() {
  return <main id="nd-docs-layout" className="home">
    <section className="home-hero" aria-labelledby="home-title">
      <div className="home-intro">
        <h1 id="home-title">The StarterKit developer&nbsp;guide</h1>
        <p className="home-lead">One ASP.NET Core 10 host, separately owned modules, boundaries enforced by tests. Open the part you are working on, or search all of it.</p>
        <SearchTrigger />
        <ContinueReading />
        <div className="home-start">
          <h2>Start a product</h2>
          <ServerCodeBlock lang="bash" code={'dotnet new install StarterKit.Templates\ndotnet new starterkit -n Acme'}
            themes={{ light: 'github-light', dark: 'github-dark' }}
            codeblock={{ title: 'Terminal', icon: <Terminal size={14} aria-hidden="true" />, className: 'home-command', viewportProps: { 'aria-label': 'Template installation and project creation commands' } }} />
          <p>Installing from the package feed? <Link href="/getting-started/templates/#install">Set up the feed first</Link>.</p>
        </div>
      </div>
      <ModuleChart modules={modules} practices={practices} families={families} />
    </section>

    <section className="home-routes" aria-labelledby="routes-title">
      <h2 id="routes-title">Three ways in</h2>
      <ol>{routes.map(({ title, description, url, action }) => <li key={title}>
        <Link href={url} className="home-route">
          <h3>{title}</h3>
          <p>{description}</p>
          <span>{action}<ArrowRight size={15} aria-hidden="true" /></span>
        </Link>
      </li>)}</ol>
    </section>

    <section className="home-proof" aria-label="How the system holds together">
      <div className="home-guardrails">
        <h2>Rules the build enforces</h2>
        <p>Architecture tests in <code>src/Api.Tests</code> fail the build when a boundary breaks.</p>
        <table>
          <thead><tr><th scope="col">Test</th><th scope="col">Fails when</th></tr></thead>
          <tbody>{guardrails.map(({ test, fails }) => <tr key={test}><td><code>{test}</code></td><td>{fails}</td></tr>)}</tbody>
        </table>
        <Link href="/reference/architecture-guardrails/" className="home-more">Every guardrail<ArrowRight size={15} aria-hidden="true" /></Link>
      </div>
      <div className="home-decisions">
        <h2>Recent decisions</h2>
        <p>Why the system is shaped this way, recorded as it changed.</p>
        <ul>{decisions.slice(0, 5).map((decision) => <li key={decision.url}>
          <Link href={decision.url}><span className="home-adr">ADR {decision.number}</span><span>{decision.title}</span></Link>
        </li>)}</ul>
        <Link href="/decisions/" className="home-more">All {decisions.length} decisions<ArrowRight size={15} aria-hidden="true" /></Link>
      </div>
    </section>

    <footer className="home-footer">
      <BrandLogo />
      <nav aria-label="Documentation resources">
        <Link href="/deployment/">Deployment</Link>
        <Link href="/reference/configuration/">Configuration</Link>
        <Link href="/llms.txt">Docs for AI agents</Link>
        <a href={`https://github.com/${repository}`}>Source on GitHub<ArrowUpRight size={13} aria-hidden="true" /></a>
      </nav>
    </footer>
  </main>;
}
