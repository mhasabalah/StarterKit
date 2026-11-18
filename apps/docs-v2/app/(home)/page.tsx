import Link from 'next/link';
import {
  ArrowUpRight, ArrowRight, ShieldCheck, Layers3, Workflow, Boxes, Terminal,
  BookOpen, Bell, CreditCard, Upload, Webhook, Activity, Server, ListChecks,
  WandSparkles, Rocket, Braces, FileCode2, FolderTree, GitBranch, Network,
  type LucideIcon,
} from 'lucide-react';
import { ServerCodeBlock } from 'fumadocs-ui/components/codeblock.rsc';
import { BrandLogo } from '@/components/brand';
import { absoluteUrl } from '@/lib/site';
import type { Metadata } from 'next';

export const metadata: Metadata = {
  description: 'Build, extend and operate a StarterKit product. Guides to ASP.NET Core modules, tenant isolation, messaging and deployment.',
  alternates: { canonical: absoluteUrl('/') },
};

type Guide = { title: string; description: string; url: string; icon: LucideIcon };
const groups: { title: string; label: string; guides: Guide[] }[] = [
  { title: 'Product foundations', label: '01', guides: [
    { title: 'Identity & authentication', description: 'JWT, permissions, passkeys and enterprise SSO.', url: '/identity/', icon: ShieldCheck },
    { title: 'Multi-tenancy', description: 'Tenants, branches and database isolation.', url: '/multitenancy/overview/', icon: Layers3 },
    { title: 'Billing & subscriptions', description: 'Plans, invoices, quotas and payment flows.', url: '/modules/subscriptions/', icon: CreditCard },
    { title: 'Attachments', description: 'Validated uploads, malware scanning and storage.', url: '/attachment/', icon: Upload },
  ] },
  { title: 'Connected systems', label: '02', guides: [
    { title: 'Notifications', description: 'Email, WhatsApp, an in-app inbox and push.', url: '/modules/notification/', icon: Bell },
    { title: 'Outbox & sagas', description: 'Transactional events and durable workflows.', url: '/architecture/messaging/', icon: Workflow },
    { title: 'Bulk import & export', description: 'Background CSV and Excel processing.', url: '/modules/datatransfer/', icon: Boxes },
    { title: 'Webhooks', description: 'Signed deliveries, retries and delivery logs.', url: '/modules/webhooks/', icon: Webhook },
  ] },
  { title: 'Engineering & operations', label: '03', guides: [
    { title: 'Observability', description: 'Traces, metrics, logs and local orchestration.', url: '/architecture/observability/', icon: Activity },
    { title: 'Deployment', description: 'Containers, migrations, backups and rollback.', url: '/deployment/', icon: Server },
    { title: 'Architecture guardrails', description: 'Tests that enforce the system boundaries.', url: '/testing/testing-strategy/', icon: ListChecks },
    { title: 'Claude Code plugin', description: 'Repository-aware skills for everyday changes.', url: '/getting-started/new-project/#6-build-your-first-module', icon: WandSparkles },
  ] },
];

const paths = [
  { title: 'Start a new product', description: 'Generate your solution, choose the defaults and get your local environment running.', url: '/getting-started/new-project/', icon: Rocket, label: 'GET STARTED', action: 'Follow the setup guide' },
  { title: 'Understand the architecture', description: 'Trace a request through the layers and see how modules work through contracts.', url: '/architecture/overview/', icon: Layers3, label: 'LEARN THE SYSTEM', action: 'Explore the architecture' },
  { title: 'Extend with confidence', description: 'Build your first module, then follow the conventions that keep its boundaries clear.', url: '/getting-started/new-project/#6-build-your-first-module', icon: GitBranch, label: 'BUILD A FEATURE', action: 'Build your first module' },
];

export default function Home() {
  return <main id="nd-docs-layout" className="home">
    <section className="home-hero" aria-labelledby="home-title">
      <div className="home-hero-copy">
        <p className="home-eyebrow"><span aria-hidden="true" /> THE STARTERKIT DEVELOPER GUIDE</p>
        <h1 id="home-title">Your next product.<br /><span>A clear foundation.</span></h1>
        <p className="home-lead">One host. Clear module boundaries.<br className="home-desktop-break" /> A practical guide to building, extending and operating your ASP.NET Core product.</p>
        <div className="home-actions">
          <Link href="/getting-started/new-project/" className="button-primary">Start building <ArrowRight size={17} aria-hidden="true" /></Link>
          <Link href="/architecture/overview/" className="button-secondary"><BookOpen size={17} aria-hidden="true" /> Explore the architecture</Link>
        </div>
        <ul className="home-stack" aria-label="Core technologies">
          <li>ASP.NET Core 10</li><li>PostgreSQL</li><li>OpenTelemetry</li>
        </ul>
      </div>
      <div className="home-workbench">
        <div className="home-workbench-heading"><span><Terminal size={16} aria-hidden="true" /> YOUR FIRST PROJECT</span><span className="home-workbench-badge">.NET 10</span></div>
        <div className="home-workbench-intro"><h2>Make it yours.</h2><p>Install the templates. Create your product.</p></div>
        <ServerCodeBlock lang="bash" code={'dotnet new install StarterKit.Templates\ndotnet new starterkit -n Acme'}
          themes={{ light: 'github-light', dark: 'github-dark' }}
          codeblock={{ title: 'Terminal', icon: <Terminal size={14} aria-hidden="true" />, className: 'home-command', viewportProps: { 'aria-label': 'Template installation and project creation commands' } }} />
        <p className="home-feed-note">Using the published package? <Link href="/getting-started/templates/#install">Set up the package feed first <ArrowUpRight size={12} aria-hidden="true" /></Link></p>
        <div className="home-project" role="group" aria-label="Generated project structure">
          <div className="home-project-title"><FolderTree size={15} aria-hidden="true" /><span>Acme / src</span><span>ONE SOLUTION</span></div>
          <dl>
            <div><dt><FileCode2 size={14} aria-hidden="true" /> Api</dt><dd>Application host</dd></div>
            <div><dt><Boxes size={14} aria-hidden="true" /> Modules</dt><dd>Your business capabilities</dd></div>
            <div><dt><Network size={14} aria-hidden="true" /> Shared</dt><dd>Contracts & infrastructure</dd></div>
          </dl>
        </div>
        <Link href="/getting-started/new-project/" className="home-workbench-footer">Continue with configuration & verification <ArrowRight size={16} aria-hidden="true" /></Link>
      </div>
    </section>

    <section className="home-paths" aria-labelledby="paths-title">
      <div className="home-section-heading"><div><p className="home-eyebrow">A GOOD PLACE TO BEGIN</p><h2 id="paths-title">Find your next step.</h2></div><p>New to StarterKit or building on it?<br />Pick the guide that meets you where you are.</p></div>
      <div className="home-path-grid">{paths.map(({ title, description, url, icon: Icon, label, action }) => <Link key={title} href={url} className="home-path">
        <span className="home-path-top"><span className="home-icon"><Icon size={21} aria-hidden="true" /></span><span>{label}</span><ArrowUpRight size={17} aria-hidden="true" /></span>
        <h3>{title}</h3><p>{description}</p><span className="home-path-action">{action}<ArrowRight size={15} aria-hidden="true" /></span>
      </Link>)}</div>
    </section>

    <section className="home-platform" aria-labelledby="platform-title">
      <div className="home-section-heading"><div><p className="home-eyebrow">EXPLORE THE PLATFORM</p><h2 id="platform-title">The pieces. And how they fit.</h2></div><Link href="/reference/features/" className="home-text-link">View the feature catalog <ArrowRight size={16} aria-hidden="true" /></Link></div>
      <div className="home-directory">{groups.map(({ title, label, guides }) => <div className="home-guide-group" key={title}>
        <h3><span aria-hidden="true">{label}</span>{title}</h3>
        {guides.map(({ title, description, url, icon: Icon }) => <Link key={title} href={url} className="home-guide">
          <span className="home-guide-icon"><Icon size={19} aria-hidden="true" /></span><div><h4>{title}</h4><p>{description}</p></div><ArrowUpRight size={15} className="home-guide-arrow" aria-hidden="true" />
        </Link>)}
      </div>)}</div>
    </section>

    <section className="home-principles" aria-labelledby="principles-title">
      <div><p className="home-eyebrow">BUILT AROUND CLEAR BOUNDARIES</p><h2 id="principles-title">Understand the decisions.<br /><span>Own what comes next.</span></h2><p>A modular monolith with explicit contracts and independently owned schemas. The reasoning is documented alongside the implementation.</p><Link href="/decisions/" className="home-text-link">Read the architecture decisions <ArrowRight size={16} aria-hidden="true" /></Link></div>
      <ul>
        <li><ShieldCheck size={20} aria-hidden="true" /><div><h3>Boundaries you can enforce</h3><p>Modules communicate through contracts and integration events. Architecture tests protect the rules.</p></div></li>
        <li><Workflow size={20} aria-hidden="true" /><div><h3>Reliability you can trace</h3><p>Follow durable messaging, tenant isolation and deployment-time migrations through their guides.</p></div></li>
        <li><Braces size={20} aria-hidden="true" /><div><h3>Contracts you can work with</h3><p>Explore the API reference generated from the application’s OpenAPI specification.</p><Link href="/api-reference/v1/openapi.json">Open the specification <ArrowUpRight size={13} aria-hidden="true" /></Link></div></li>
      </ul>
    </section>

    <section className="home-next" aria-labelledby="next-title"><div><span className="home-icon"><Rocket size={23} aria-hidden="true" /></span><div><h2 id="next-title">From foundation to your first feature.</h2><p>Start with a working product. Make room for your domain.</p></div></div><Link href="/getting-started/new-project/" className="button-primary">Start your project <ArrowRight size={17} aria-hidden="true" /></Link></section>
    <footer className="home-footer"><div className="home-footer-brand"><BrandLogo /><span>Developer documentation</span></div><nav aria-label="Documentation resources"><Link href="/deployment/">Deployment guide</Link><Link href="/decisions/">Architecture decisions</Link><Link href="/llms.txt">Docs for AI <ArrowUpRight size={12} aria-hidden="true" /></Link></nav></footer>
  </main>;
}
