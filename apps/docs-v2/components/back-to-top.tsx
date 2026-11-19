'use client';
import { useSyncExternalStore } from 'react';
import { ArrowUp } from 'lucide-react';

function subscribe(onChange: () => void) {
  window.addEventListener('scroll', onChange, { passive: true });
  return () => window.removeEventListener('scroll', onChange);
}
/** Shown once the reader is more than a screen and a half down, where the top is no longer one flick away. */
const away = () => window.scrollY > window.innerHeight * 1.5;

export function BackToTop() {
  const visible = useSyncExternalStore(subscribe, away, () => false);
  function top() {
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    window.scrollTo({ top: 0, behavior: reduce ? 'auto' : 'smooth' });
    // Keyboard and screen-reader users land on the page title, not on a button that just disappeared.
    const heading = document.querySelector<HTMLElement>('main h1');
    if (heading) { heading.tabIndex = -1; heading.focus({ preventScroll: true }); }
  }
  // Inert while hidden: out of the tab order and the accessibility tree, so it never takes a stray focus or tap.
  return <button type="button" className="back-to-top" data-visible={visible || undefined} inert={!visible}
    onClick={top} aria-label="Back to top">
    <ArrowUp aria-hidden="true" />
  </button>;
}
