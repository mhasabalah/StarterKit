import Link from 'next/link';
import { ViewTransition } from 'react';
import type { ChartElement } from '@/lib/chart';

/** The page's place on the module chart, set beside its title. Pages outside the chart get no tile. */
export function ElementTile({ element, api }: { element?: ChartElement; api?: boolean }) {
  if (api) return <span className="element-tile" data-family="api" aria-hidden="true">
    <span className="element-tile-meta">v1</span><span className="element-tile-symbol">Ap</span>
  </span>;
  if (!element) return null;
  const order = element.order === undefined ? undefined : String(element.order).padStart(3, '0');
  // Named like the home chart's tile for the same element, so navigation morphs one into the other.
  return <ViewTransition name={`element-${element.symbol}`} share="element-morph" default="none"><Link href={element.url} className="element-tile" data-family={element.family}
    aria-label={`${element.name} section${order ? `, startup order ${order}` : ''}, ${element.pages} pages`}>
    <span className="element-tile-meta" aria-hidden="true"><span>{order}</span><span>{element.pages}</span></span>
    <span className="element-tile-symbol" aria-hidden="true">{element.symbol}</span>
  </Link></ViewTransition>;
}

/** A small symbol chip for the navigation tree. */
export function ElementChip({ symbol, family }: { symbol: string; family: string }) {
  return <span className="element-chip" data-family={family} aria-hidden="true">{symbol}</span>;
}
