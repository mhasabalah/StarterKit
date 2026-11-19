'use client';
import { useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';
import { ChevronDown } from 'lucide-react';
import { TOCItem, useActiveAnchor, type TOCItemType } from 'fumadocs-core/toc';

/** Reading progress through the page, 0 to 1, updated at most once per frame. */
function subscribe(onChange: () => void) {
  let frame = 0;
  const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(onChange); };
  window.addEventListener('scroll', schedule, { passive: true });
  window.addEventListener('resize', schedule);
  return () => { cancelAnimationFrame(frame); window.removeEventListener('scroll', schedule); window.removeEventListener('resize', schedule); };
}
function progress() {
  const max = document.documentElement.scrollHeight - window.innerHeight;
  return max > 0 ? Math.min(1, Math.max(0, Math.round((window.scrollY / max) * 100) / 100)) : 0;
}

function Ring({ value }: { value: number }) {
  const r = 7, c = 2 * Math.PI * r;
  return <svg className="section-ring" viewBox="0 0 18 18" aria-hidden="true">
    <circle cx="9" cy="9" r={r} className="section-ring-track" />
    <circle cx="9" cy="9" r={r} className="section-ring-value" strokeDasharray={c} strokeDashoffset={c * (1 - value)} />
  </svg>;
}

/**
 * "On this page" below the wide layout: a sticky bar under the header naming the section being read, with reading
 * progress; it opens to the full outline. It reads the active heading from the same Fumadocs TOC state as the
 * desktop outline, so both always agree.
 */
export function SectionBar({ toc }: { toc: TOCItemType[] }) {
  const active = useActiveAnchor();
  const value = useSyncExternalStore(subscribe, progress, () => 0);
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const list = useId();
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false); ref.current?.querySelector('button')?.focus(); } };
    const onPointer = (event: PointerEvent) => { if (!ref.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener('keydown', onKey);
    document.addEventListener('pointerdown', onPointer);
    return () => { document.removeEventListener('keydown', onKey); document.removeEventListener('pointerdown', onPointer); };
  }, [open]);
  if (toc.length === 0) return null;
  const index = toc.findIndex((item) => item.url === `#${active}`);
  const current = index >= 0 ? toc[index] : undefined;
  return <div ref={ref} className="section-bar" data-open={open || undefined}>
    <button type="button" className="section-bar-trigger" aria-expanded={open} aria-controls={list} onClick={() => setOpen(!open)}>
      <Ring value={value} />
      <span className="section-bar-label">
        <span className="sr-only">On this page{current ? ', current section: ' : ''}</span>
        {current ? current.title : <span aria-hidden="true">On this page</span>}
      </span>
      {current && <span className="section-bar-count" aria-hidden="true">{index + 1}/{toc.length}</span>}
      <ChevronDown className="section-bar-chevron" aria-hidden="true" />
    </button>
    <nav id={list} className="section-bar-panel" aria-label="On this page" hidden={!open}>
      <ol>{toc.map((item) => <li key={item.url} style={{ '--depth': Math.max(0, item.depth - 2) } as React.CSSProperties}>
        <TOCItem href={item.url} onClick={() => setOpen(false)}>{item.title}</TOCItem>
      </li>)}</ol>
    </nav>
  </div>;
}
