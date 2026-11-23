'use client';
import Link from 'next/link';
import { useEffect, useRef, useState, type CSSProperties, type PointerEvent } from 'react';
import { ArrowRight, ArrowUpRight, Check } from 'lucide-react';
import { InView } from '@/components/in-view';
import type { Family } from '@/lib/chart';

export type Capability = { symbol: string; title: string; detail: string; family: Family; url: string };

/** The architecture in three rules, each one a rule the code keeps. */
const rules = [
  'One deployable host; modules are discovered from their manifests.',
  'Each module owns its schema and talks only through contracts and outbox events.',
  'Architecture tests fail the build when a boundary breaks.',
];

/** The stack, from Directory.Packages.props, global.json and the Postgres image (versions on the stack page). */
const stack = [
  'ASP.NET Core 10', 'EF Core 10', 'PostgreSQL 16', 'pgvector', 'MassTransit 8.5', 'Transactional outbox', 'Hangfire 1.8',
  'Redis', 'SignalR', 'Carter 10', 'FluentValidation 12', 'Scalar', 'OpenTelemetry', '.NET Aspire', 'Docker', 'Nginx',
  'GitHub Actions', 'xUnit v3', 'Testcontainers',
];

/** Module folders the template generates (src/Modules) and the resources the Aspire AppHost starts. */
const folders = ['Identity', 'Tenancy', 'Attachment', 'Catalog', 'Subscriptions', 'Webhooks', 'DataTransfer', 'Shipping', 'Agentic', 'Notification'];
const resources = ['api', 'postgres', 'redis', 'seq'];

function Terminal() {
  return <InView className="why-terminal" threshold={0.4}>
    <div className="why-terminal-bar"><span>Acme</span><span>what the template generates</span></div>
    <p className="sr-only">Running dotnet new starterkit -n Acme generates these module folders under src/Modules: {folders.join(', ')}.
      Running the Aspire AppHost starts {resources.join(', ')}.</p>
    <div className="why-terminal-body" aria-hidden="true">
      <span className="why-cmd"><i>$</i> dotnet new starterkit -n Acme</span>
      <span className="why-out why-out-head">Acme/src/Modules/</span>
      <span className="why-out why-folders">{folders.map((folder, index) => <span key={folder} style={{ '--f': index } as CSSProperties}>{folder}</span>)}</span>
      <span className="why-cmd"><i>$</i> dotnet run --project src/Aspire/AppHost</span>
      <span className="why-out why-resources">{resources.map((resource, index) => <span key={resource} style={{ '--f': index } as CSSProperties}><b />{resource}</span>)}</span>
      <span className="why-cmd why-cmd-last"><i>$</i> <span className="why-caret" /></span>
    </div>
  </InView>;
}

/** A light that follows the pointer across the tiles; each tile reads its own local position. */
function Tiles({ items }: { items: Capability[] }) {
  const grid = useRef<HTMLUListElement>(null);
  const move = (event: PointerEvent<HTMLUListElement>) => {
    for (const tile of grid.current?.querySelectorAll<HTMLElement>('.why-tile') ?? []) {
      const rect = tile.getBoundingClientRect();
      tile.style.setProperty('--mx', `${event.clientX - rect.left}px`);
      tile.style.setProperty('--my', `${event.clientY - rect.top}px`);
    }
  };
  return <ul ref={grid} className="why-tiles" onPointerMove={move} aria-label="Ready on day one">
    {items.map(({ symbol, title, detail, family, url }) => <li key={symbol}>
      <Link href={url} className="why-tile" data-family={family}>
        <span className="why-tile-top" aria-hidden="true"><span className="why-symbol">{symbol}</span><ArrowUpRight size={16} /></span>
        <b>{title}</b>
        <span className="why-detail">{detail}</span>
      </Link>
    </li>)}
  </ul>;
}

export function WhyShowcase({ capabilities }: { capabilities: Capability[] }) {
  // The ribbon and the caret loop only while the band is on screen.
  const band = useRef<HTMLElement>(null);
  const [onScreen, setOnScreen] = useState(false);
  useEffect(() => {
    const element = band.current;
    if (!element) return;
    const observer = new IntersectionObserver(([entry]) => setOnScreen(entry.isIntersecting));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return <section ref={band} className="home-why" aria-labelledby="why-title" data-onscreen={onScreen || undefined}>
    <div className="why-top">
      <div className="why-pitch">
        <h2 id="why-title">Ship the product, not the plumbing.</h2>
        <p className="why-lead">StarterKit is a .NET 10 template for multi-tenant SaaS. Generate it and identity, tenancy, billing, messaging and observability are already wired, tested and documented. Your first sprint goes on your product.</p>
        <ul className="why-rules" aria-label="The architecture in three rules">
          {rules.map((rule) => <li key={rule}><Check size={14} strokeWidth={3} aria-hidden="true" />{rule}</li>)}
        </ul>
        <div className="why-actions">
          <Link href="/getting-started/new-project/" className="why-primary">Generate your product<ArrowRight size={16} aria-hidden="true" /></Link>
          <Link href="/architecture/overview/" className="why-secondary">How it fits together</Link>
        </div>
      </div>
      <Terminal />
    </div>

    <h3 className="why-subhead">Ready on day one</h3>
    <Tiles items={capabilities} />

    <div className="why-stack">
      <h3 className="sr-only">Built on</h3>
      <div className="why-ribbon">
        <ul className="why-ribbon-track" aria-label="Built on">
          {stack.map((part) => <li key={part}>{part}</li>)}
        </ul>
        <ul className="why-ribbon-track" aria-hidden="true">
          {stack.map((part) => <li key={part}>{part}</li>)}
        </ul>
      </div>
      <Link href="/architecture/technologystack/" className="why-stack-link">Every package, with its version<ArrowRight size={15} aria-hidden="true" /></Link>
    </div>
  </section>;
}
