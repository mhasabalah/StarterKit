import Link from 'next/link';
import { ArrowRight, ArrowUpRight, Check, Terminal } from 'lucide-react';
import { ServerCodeBlock } from 'fumadocs-ui/components/codeblock.rsc';
import { BrandLogo } from '@/components/brand';
import { ModuleChart, ContinueReading } from '@/components/module-chart';
import { SearchTrigger } from '@/components/search-trigger';
import { CopyStartPrompt } from '@/components/agent-copy';
import { RequestJourney } from '@/components/request-journey';
import { InView } from '@/components/in-view';
import { RoutePlate } from '@/components/route-plate';
import { WhyShowcase } from '@/components/why-showcase';
import { families, modules, practices } from '@/lib/chart';
import { pageTree, source } from '@/lib/source';
import type * as PageTree from 'fumadocs-core/page-tree';
import { absoluteUrl, repository } from '@/lib/site';
import type { Metadata } from 'next';
import type { CSSProperties } from 'react';

export const metadata: Metadata = {
  description: 'Build, extend and operate a StarterKit product. Guides to ASP.NET Core modules, tenant isolation, messaging and deployment.',
  alternates: { canonical: absoluteUrl('/') },
};

const routes = [
  { title: 'Start a new product', description: 'Generate the solution, choose the defaults and run the stack locally with Aspire.', url: '/getting-started/new-project/', action: 'Setup guide', plate: 'start' },
  { title: 'Understand the architecture', description: 'Follow a request through the host, the module boundaries and the contracts between them.', url: '/architecture/overview/', action: 'Architecture overview', plate: 'architecture' },
  { title: 'Build your first module', description: 'Scaffold a module with dotnet new starterkit-module and keep to the conventions the tests enforce.', url: '/getting-started/new-project/#6-build-your-first-module', action: 'First module', plate: 'module' },
] as const;

/** The API reference has no landing page: link to the first operation group in its navigation, so the link always resolves. */
function firstPage(nodes: PageTree.Node[]): string | undefined {
  for (const node of nodes) {
    if (node.type === 'page') return node.url;
    if (node.type === 'folder') { const url = node.index?.url ?? firstPage(node.children); if (url) return url; }
  }
}
const apiFolder = pageTree.children.find((node): node is PageTree.Folder => node.type === 'folder' && node.name === 'API reference v1');
const apiReferenceUrl = (apiFolder && firstPage(apiFolder.children)) ?? '/api-reference/v1/openapi.json';

const footerColumns: { title: string; links: { label: string; href: string; external?: boolean }[] }[] = [
  { title: 'Build', links: [
    { label: 'New project guide', href: '/getting-started/new-project/' },
    { label: 'Architecture overview', href: '/architecture/overview/' },
    { label: 'Multi-tenancy', href: '/multitenancy/overview/' },
    { label: 'Testing strategy', href: '/testing/testing-strategy/' },
  ] },
  { title: 'Operate', links: [
    { label: 'Deployment', href: '/deployment/' },
    { label: 'Configuration', href: '/reference/configuration/' },
    { label: 'Local stack with Aspire', href: '/infrastructure/aspire/' },
    { label: 'API reference', href: apiReferenceUrl },
  ] },
  { title: 'Resources', links: [
    { label: 'Architecture decisions', href: '/decisions/' },
    { label: 'Every guardrail', href: '/reference/architecture-guardrails/' },
    { label: 'Docs for AI agents', href: '/llms.txt' },
    { label: 'Source on GitHub', href: `https://github.com/${repository}`, external: true },
  ] },
];

/** Written guides only; the generated API reference pages are not counted. */
const pageCount = source.getPages().filter((page) => !page.url.startsWith('/api-reference/')).length;

/** Real guardrails from src/Api.Tests/Architecture (see /reference/architecture-guardrails/). */
const guardrails = [
  { test: 'ModuleBoundaryTests', fails: 'A module references another module’s implementation instead of its contracts.' },
  { test: 'TenantIsolationArchitectureTests', fails: 'A tenant entity lacks IMustHaveTenant, or a unique index does not start with tenant_id.' },
  { test: 'MessagingArchitectureTests', fails: 'An integration event lives outside .Contracts or reuses a message URN.' },
  { test: 'AdminEndpointAuthorizationTests', fails: 'Any endpoint declares no authorization at all.' },
  { test: 'PendingModelChangesTests', fails: 'The EF model changed without a migration.' },
];

/**
 * What a team gets on day one. Each row is a chart element (so its family, symbol and guide are real and the
 * row disappears if the element has no pages); the detail names only what the module ships.
 */
const capabilities: { symbol: string; title: string; detail: string }[] = [
  { symbol: 'Id', title: 'Identity', detail: 'JWT with rotating refresh tokens, roles and permissions, 2FA, passkeys, SSO (OIDC and SAML), SCIM, API keys.' },
  { symbol: 'Tn', title: 'Multi-tenancy', detail: 'Tenants, branches and memberships on a shared or a dedicated database, isolated by row-level security.' },
  { symbol: 'Sb', title: 'Billing', detail: 'Plans, entitlements and quotas, a ledger, PDF invoices and Stripe payments.' },
  { symbol: 'At', title: 'Files', detail: 'Local, Azure Blob or S3 storage, image processing and malware scanning that fails closed.' },
  { symbol: 'Nt', title: 'Notifications', detail: 'Email and WhatsApp with fallback, an in-app inbox over SignalR, and push.' },
  { symbol: 'Wh', title: 'Webhooks', detail: 'Signed outbound webhooks per tenant, with retries and a delivery log.' },
  { symbol: 'Dt', title: 'Bulk import and export', detail: 'CSV and Excel jobs with a dry run, an error report and resume after a crash.' },
  { symbol: 'Se', title: 'Full-text search', detail: 'Ranked, faceted, typo-tolerant search in English and Arabic for any module, on PostgreSQL or Meilisearch.' },
  { symbol: 'Ag', title: 'AI agents', detail: 'Durable agent runs with typed tools, approvals, knowledge on pgvector and an MCP server.' },
  { symbol: 'Ar', title: 'Observability', detail: 'OpenTelemetry traces, metrics and logs, with Grafana, Tempo, Loki and Prometheus locally.' },
  { symbol: 'Dp', title: 'Deployment', detail: 'Docker and Nginx, a GitHub Actions pipeline with an approval gate, migrations at deploy, rollback.' },
];
const elements = [...modules, ...practices];
const dayOne = capabilities.flatMap((capability) => {
  const element = elements.find((candidate) => candidate.symbol === capability.symbol);
  return element ? [{ ...capability, family: element.family, url: element.url }] : [];
});

/** Long test names may wrap only between words of their PascalCase name. */
const breakable = (name: string) => name.split(/(?=[A-Z])/).flatMap((part, index) => index === 0 ? [part] : [<wbr key={index} />, part]);

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
          <CopyStartPrompt />
        </div>
      </div>
      <ModuleChart modules={modules} practices={practices} families={families} />
    </section>

    <WhyShowcase capabilities={dayOne} />

    <section className="home-routes" aria-labelledby="routes-title">
      <div className="home-stratum-head">
        <h2 id="routes-title">Three ways in</h2>
        <p>Pick the job in front of you. Each path ends in a guide that names the commands, the files and the rules.</p>
      </div>
      <ol>{routes.map(({ title, description, url, action, plate }) => <li key={title}>
        <Link href={url} className="home-route" data-plate={plate}>
          <RoutePlate kind={plate} />
          <h3>{title}</h3>
          <p>{description}</p>
          <span>{action}<ArrowRight size={15} aria-hidden="true" /></span>
        </Link>
      </li>)}</ol>
    </section>

    <section className="home-journey" aria-labelledby="journey-title">
      <div className="home-stratum-head">
        <h2 id="journey-title">Follow one request through the system</h2>
        <p>A <code>POST /api/v1/products</code> from the client to the response, and the event it leaves behind. Every station is a real boundary, in the order the code runs it.</p>
      </div>
      <RequestJourney />
    </section>

    <section className="home-proof" aria-label="How the system holds together">
      <InView className="home-guardrails">
        <h2>Rules the build enforces</h2>
        <p>Architecture tests in <code>src/Api.Tests</code> fail the build when a boundary breaks, so the conventions on these pages are checked, not hoped for.</p>
        <div className="home-run">
          <p className="home-run-command" aria-hidden="true"><span>$</span> dotnet test src/Api.Tests</p>
          <table>
            <thead><tr><th scope="col">Test</th><th scope="col">Fails when</th></tr></thead>
            <tbody>{guardrails.map(({ test, fails }, index) => <tr key={test} style={{ '--i': index } as CSSProperties}>
              <td><span className="home-test"><span className="home-tick" aria-hidden="true"><Check size={12} strokeWidth={3} /></span><code>{breakable(test)}</code></span></td><td>{fails}</td>
            </tr>)}</tbody>
          </table>
        </div>
        <Link href="/reference/architecture-guardrails/" className="home-more">Every guardrail<ArrowRight size={15} aria-hidden="true" /></Link>
      </InView>
      <div className="home-decisions">
        <h2>Recent decisions</h2>
        <p>Why the system is shaped this way, recorded as it changed.</p>
        <ul>{decisions.slice(0, 5).map((decision) => <li key={decision.url}>
          <Link href={decision.url}><span className="home-adr">ADR {decision.number}</span><span>{decision.title}</span><ArrowRight size={15} aria-hidden="true" /></Link>
        </li>)}</ul>
        <Link href="/decisions/" className="home-more">All {decisions.length} decisions<ArrowRight size={15} aria-hidden="true" /></Link>
      </div>
    </section>

    <footer className="home-footer">
      <div className="home-colophon">
        <BrandLogo />
        <p>The developer guide to a modular monolith on ASP.NET Core 10, written beside the code it describes.</p>
        <dl className="home-edition">
          <div><dt>Guides</dt><dd>{pageCount}</dd></div>
          <div><dt>Modules</dt><dd>{modules.length}</dd></div>
          <div><dt>Decisions</dt><dd>{decisions.length}</dd></div>
        </dl>
      </div>
      {footerColumns.map((column) => <nav key={column.title} aria-label={column.title === 'Resources' ? 'Documentation resources' : `${column.title} guides`}>
        <h2>{column.title}</h2>
        <ul>{column.links.map((link) => <li key={link.label}>
          {link.external
            ? <a href={link.href}>{link.label}<ArrowUpRight size={13} aria-hidden="true" /></a>
            : <Link href={link.href}>{link.label}</Link>}
        </li>)}</ul>
      </nav>)}
      <p className="home-footnote">Generated from <code>wiki/</code> in the StarterKit repository. The counts above are taken from the pages at build time.</p>
    </footer>
  </main>;
}
