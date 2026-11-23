'use client';
import dynamic from 'next/dynamic';
import { useEffect, useRef, useState } from 'react';
import { useTheme } from 'next-themes';
import { Expand, FileCode2 } from 'lucide-react';

// The generated module (likec4 gen react, scripts/architecture.mjs) bundles the model, layouts and icons. It loads only
// on pages that render a view, and only in the browser: the diagram is interactive and measures the DOM.
const LikeC4View = dynamic(() => import('@/.generated/likec4/views.mjs').then((module) => module.LikeC4View), {
  ssr: false,
  loading: () => <p role="status" className="architecture-status">Loading architecture view…</p>,
});

type Props = { id: string; title: string; description: string; source?: string; width?: string };

/**
 * One view of the architecture model (ADR-0035). The inline view is a fitted overview, capped at its natural width;
 * clicking it, or Explore, opens LikeC4's browser: zoom, search, element details, drill-down and dynamic walkthroughs.
 */
export function ArchitectureView({ id, title, description, source, width }: Props) {
  const { resolvedTheme } = useTheme();
  const canvas = useRef<HTMLDivElement>(null);
  const [state, setState] = useState<'loading' | 'ready'>('loading');
  const [browsing, setBrowsing] = useState(false);
  useEffect(() => {
    const host = canvas.current;
    if (!host) return;
    // LikeC4 renders inside a shadow root. The inline view mirrors the caption, so it stays out of the tab order and
    // the accessibility tree; the Explore button is the accessible way in. LikeC4's browser opens as a <dialog> in the
    // same shadow root, so the canvas is exposed again while that dialog is open.
    let observer: MutationObserver | undefined;
    const sync = (root: ShadowRoot) => {
      setBrowsing(Boolean(root.querySelector('dialog.likec4-overlay[open]')));
      root.querySelectorAll<HTMLElement>('a[href], button, input, select, textarea, [tabindex]').forEach((element) => {
        if (!element.closest('dialog') && element.getAttribute('tabindex') !== '-1') element.setAttribute('tabindex', '-1');
      });
    };
    const timer = setInterval(() => {
      const root = host.querySelector('.likec4-view')?.shadowRoot;
      if (!root?.querySelector('.react-flow__node')) return;
      clearInterval(timer);
      sync(root);
      observer = new MutationObserver(() => sync(root));
      observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['open', 'tabindex'] });
      setState('ready');
    }, 150);
    return () => { clearInterval(timer); observer?.disconnect(); };
  }, []);
  function explore() {
    const root = canvas.current?.querySelector('.likec4-view')?.shadowRoot;
    (root?.querySelector('.react-flow__pane') as HTMLElement | null)?.click();
  }
  const titleId = `architecture-${id}-title`;
  return <figure className="architecture-view" data-diagram-state={state} data-view-id={id} aria-labelledby={titleId}>
    <div className="diagram-toolbar"><span>ARCHITECTURE VIEW</span><div>
      {source && <a href={source} aria-label="View model source on GitHub"><FileCode2 size={15} /></a>}
      <button type="button" onClick={explore} disabled={state !== 'ready'} aria-label={`Explore ${title} interactively`}><Expand size={15} /></button>
    </div></div>
    <div ref={canvas} className="architecture-canvas" style={{ maxWidth: width ? `${Math.max(Number(width) * 0.6, 320)}px` : undefined }} aria-hidden={browsing ? undefined : true}>
      <LikeC4View viewId={id as never} colorScheme={resolvedTheme === 'dark' ? 'dark' : 'light'} injectFontCss={false}
        enableElementDetails enableRelationshipDetails enableDynamicViewWalkthrough enableNotations
        reactFlowProps={{ nodesFocusable: false, edgesFocusable: false }} />
    </div>
    <figcaption><strong id={titleId}>{title}.</strong> {description} <span className="architecture-hint">Click the view or Explore to zoom, search and drill down.</span></figcaption>
  </figure>;
}
