'use client';
import { useEffect, useId, useRef, useState } from 'react';
import { useTheme } from 'next-themes';
import { Copy, Download, Expand, Minus, Plus, X } from 'lucide-react';
import { diagramTheme } from '@/lib/mermaid-theme.mjs';

// Mermaid has global configuration. Queue initialization and rendering together so themes cannot race.
let queue: Promise<unknown> = Promise.resolve();
let renderSequence = 0;
export function Mermaid({ code }: { code: string }) {
  const id = useId().replace(/[^a-zA-Z0-9]/g, '');
  const { resolvedTheme } = useTheme();
  const [result, setResult] = useState<{ svg?: string; error?: string }>({});
  const [status, setStatus] = useState('');
  const [zoom, setZoom] = useState(1);
  const dialog = useRef<HTMLDialogElement>(null);
  const figure = useRef<HTMLDivElement>(null);
  const expanded = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    let cancelled = false;
    const render = async () => {
      try {
        const { default: mermaid } = await import('mermaid');
        await document.fonts.ready;
        mermaid.initialize(diagramTheme(resolvedTheme === 'dark'));
        const { svg } = await mermaid.render(`diagram-${id}-${++renderSequence}`, code);
        if (!cancelled) setResult({ svg });
      } catch (error) {
        if (!cancelled) setResult({ error: error instanceof Error ? error.message : 'Unable to render diagram' });
      }
    };
    queue = queue.then(render, render);
    return () => { cancelled = true; };
  }, [code, id, resolvedTheme]);
  // Move the single SVG into the dialog, preserving IDs rather than cloning it.
  useEffect(() => {
    const container = figure.current;
    if (!container || !result.svg) return;
    const host = dialog.current?.open ? expanded.current : container;
    if (host) {
      host.innerHTML = result.svg;
      const width = host.querySelector('svg')?.viewBox.baseVal.width;
      if (width) host.style.setProperty('--diagram-natural-width', `${width}px`);
    }
  }, [result.svg]);
  async function copy() {
    try { await navigator.clipboard.writeText(code); setStatus('Diagram source copied'); }
    catch { setStatus('Copy unavailable. Select the source below.'); }
  }
  function open() {
    setZoom(1);
    if (figure.current?.firstChild) expanded.current?.replaceChildren(figure.current.firstChild);
    dialog.current?.showModal();
  }
  function close() {
    if (expanded.current?.firstChild) figure.current?.replaceChildren(expanded.current.firstChild);
    trigger.current?.focus();
  }
  function download() {
    if (!result.svg) return;
    const url = URL.createObjectURL(new Blob([result.svg], { type: 'image/svg+xml' }));
    const a = document.createElement('a'); a.href = url; a.download = 'architecture-diagram.svg'; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  const title = code.match(/^\s*accTitle:\s*(.+)$/m)?.[1] || 'Architecture diagram';
  return <figure className="diagram" aria-label={title} data-diagram-state={result.error ? 'error' : result.svg ? 'ready' : 'loading'}>
    <div className="diagram-toolbar"><span>DIAGRAM</span><div>
      <button type="button" onClick={copy} aria-label="Copy diagram source"><Copy size={15} /></button>
      <button type="button" onClick={download} disabled={!result.svg} aria-label="Download diagram SVG"><Download size={15} /></button>
      <button type="button" ref={trigger} onClick={open} disabled={!result.svg} aria-label="Expand diagram"><Expand size={15} /></button>
    </div></div>
    {result.error ? <p role="alert">Diagram could not render: {result.error}. The original source is available below.</p> : !result.svg ? <p role="status">Rendering diagram…</p> : null}
    <div ref={figure} className="diagram-canvas" role="region" tabIndex={0} aria-label={`${title}. Scroll to explore; text source follows.`} />
    <p className="diagram-hint">Scroll to explore the diagram, or use Expand.</p>
    <details><summary>View diagram source</summary><pre tabIndex={0}><code>{code}</code></pre></details>
    <span role="status" className="sr-only">{status}</span>
    <dialog ref={dialog} onClose={close} aria-label="Expanded diagram" className="diagram-dialog">
      <div className="diagram-toolbar"><strong>Architecture diagram</strong><div>
        <button type="button" aria-label="Zoom out" onClick={() => setZoom((value) => Math.max(0.25, value - 0.25))}><Minus size={18} /></button>
        <button type="button" onClick={() => setZoom(1)}>Reset zoom</button>
        <button type="button" aria-label="Zoom in" onClick={() => setZoom((value) => Math.min(4, value + 0.25))}><Plus size={18} /></button>
        <button type="button" aria-label="Close diagram" onClick={() => dialog.current?.close()}><X size={18} /></button>
      </div></div>
      <div className="diagram-scroll"><div ref={expanded} style={{ width: `${zoom * 100}%`, minWidth: `${zoom * 600}px` }} /></div>
    </dialog>
  </figure>;
}
