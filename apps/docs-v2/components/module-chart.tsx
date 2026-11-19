'use client';
import Link from 'next/link';
import { useMemo, useState, useSyncExternalStore, type CSSProperties } from 'react';
import { ArrowRight, Plus } from 'lucide-react';
import type { ChartElement, Family } from '@/lib/chart';
import { LAST_READ_KEY } from '@/lib/last-read';

type LastRead = { url: string; title: string };

function subscribe(onChange: () => void) {
  window.addEventListener('storage', onChange);
  return () => window.removeEventListener('storage', onChange);
}
function snapshot() {
  try { return localStorage.getItem(LAST_READ_KEY); } catch { return null; }
}
/** The guide this browser read last; nothing on the server or when storage is unavailable. */
function useLastRead(): LastRead | undefined {
  const raw = useSyncExternalStore(subscribe, snapshot, () => null);
  return useMemo(() => {
    try {
      const value = JSON.parse(raw ?? 'null');
      return value && typeof value.url === 'string' && typeof value.title === 'string' ? value : undefined;
    } catch { return undefined; }
  }, [raw]);
}
const slash = (url: string) => (url.endsWith('/') ? url : `${url}/`);
function ownerOf(url: string, elements: ChartElement[]) {
  let best: ChartElement | undefined; let length = -1;
  for (const element of elements) for (const prefix of element.prefixes) {
    if (slash(url).startsWith(prefix) && prefix.length > length) { best = element; length = prefix.length; }
  }
  return best;
}
const pad = (order: number) => String(order).padStart(3, '0');

function Tile({ element, index, lastRead }: { element: ChartElement; index: number; lastRead: boolean }) {
  const meta = element.order === undefined ? `${element.pages} pages` : `startup order ${pad(element.order)}, ${element.pages} pages`;
  return <Link href={element.url} className="tile" data-family={element.family} data-symbol={element.symbol}
    data-last-read={lastRead || undefined} style={{ '--i': index } as CSSProperties}
    aria-label={`${element.name}, ${meta}${lastRead ? ', last read' : ''}`}>
    <span className="tile-meta" aria-hidden="true">
      <span>{element.order === undefined ? '' : pad(element.order)}</span><span>{element.pages}</span>
    </span>
    <span className="tile-symbol" aria-hidden="true">{element.symbol}</span>
    <span className="tile-name" aria-hidden="true">{element.name}</span>
  </Link>;
}

export function ModuleChart({ modules, practices, families }: {
  modules: ChartElement[]; practices: ChartElement[];
  families: { id: Family; name: string; description: string }[];
}) {
  const [active, setActive] = useState<Family | null>(null);
  const lastRead = useLastRead();
  const marked = lastRead && ownerOf(lastRead.url, [...modules, ...practices]);
  const selected = families.find((family) => family.id === active);
  const before = modules.filter((element) => (element.order ?? 0) < 700);
  const after = modules.filter((element) => (element.order ?? 0) >= 700);
  let index = 0;
  return <div className="chart" data-active={active ?? undefined}>
    <div className="chart-modules" aria-labelledby="chart-title" role="group">
      <div className="chart-key">
        <h2 id="chart-title">Modules, in startup order</h2>
        <p>Each module owns its schema and talks to the others through contracts. The number is its real <code>ModuleOrder</code>; Identity registers first, Notification last.</p>
      </div>
      {before.map((element) => <div key={element.symbol} className="chart-cell" data-cell={element.symbol}>
        <Tile element={element} index={index++} lastRead={marked === element} /></div>)}
      {after.filter((element) => element.order === 700).map((element) => <div key={element.symbol} className="chart-cell" data-cell={element.symbol}>
        <Tile element={element} index={index++} lastRead={marked === element} /></div>)}
      <div className="chart-cell" data-cell="yours">
        <Link href="/getting-started/new-project/#6-build-your-first-module" className="tile tile-yours" style={{ '--i': index++ } as CSSProperties}
          aria-label="Your module, startup order 700: generate it with dotnet new starterkit-module">
          <span className="tile-meta" aria-hidden="true"><span>700</span><span /></span>
          <span className="tile-symbol" aria-hidden="true"><Plus size={30} strokeWidth={2.25} /></span>
          <span className="tile-name" aria-hidden="true">Your module</span>
          <code className="tile-command" aria-hidden="true">dotnet new starterkit-module</code>
        </Link>
      </div>
      {after.filter((element) => element.order !== 700).map((element) => <div key={element.symbol} className="chart-cell" data-cell={element.symbol}>
        <Tile element={element} index={index++} lastRead={marked === element} /></div>)}
    </div>
    <div className="chart-series">
      <h2 id="series-title">Guides across every module</h2>
      <div className="chart-practices" role="list" aria-labelledby="series-title">
        {practices.map((element) => <div role="listitem" key={element.symbol} className="chart-cell">
          <Tile element={element} index={index++} lastRead={marked === element} /></div>)}
      </div>
    </div>
    <div className="chart-legend">
      <div role="group" aria-label="Highlight a family">
        {families.map((family) => <button key={family.id} type="button" data-family={family.id} aria-pressed={active === family.id}
          onClick={() => setActive(active === family.id ? null : family.id)}>
          <span aria-hidden="true" />{family.name}
        </button>)}
      </div>
      <p aria-live="polite">{selected ? selected.description + '.' : 'Top corner: startup order and page count.'}</p>
    </div>
  </div>;
}

export function ContinueReading() {
  const lastRead = useLastRead();
  if (!lastRead) return null;
  return <p className="continue-reading">
    <span>Last read</span>
    <Link href={lastRead.url}>{lastRead.title}<ArrowRight size={14} aria-hidden="true" /></Link>
  </p>;
}
