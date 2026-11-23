'use client';
import { useEffect, useRef, useState, type ReactNode } from 'react';

/**
 * Marks its block `data-inview` the first time it is on screen, so a one-off CSS sequence can run there.
 * The server render is the finished state: without JavaScript, or with reduced motion, nothing is hidden.
 */
export function InView({ as: Tag = 'div', className, children, threshold = 0.3, ...rest }: {
  as?: 'div' | 'section'; className?: string; children: ReactNode; threshold?: number;
  'aria-labelledby'?: string; 'aria-label'?: string;
}) {
  const ref = useRef<HTMLElement>(null);
  const [state, setState] = useState<'idle' | 'ready' | 'in'>('idle');
  useEffect(() => {
    const element = ref.current;
    if (!element || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const rect = element.getBoundingClientRect();
    if (rect.top < innerHeight && rect.bottom > 0) return; // already on screen: keep the finished state, no replay
    setState('ready');
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) { setState('in'); observer.disconnect(); }
    }, { threshold });
    observer.observe(element);
    return () => observer.disconnect();
  }, [threshold]);
  return <Tag ref={ref as never} className={className} data-motion={state === 'idle' ? undefined : state} {...rest}>{children}</Tag>;
}
