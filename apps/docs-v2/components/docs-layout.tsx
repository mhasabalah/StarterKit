'use client';
import { useEffect, useRef, useState } from 'react';
import { usePathname } from 'fumadocs-core/framework';
import { AIChatPanel } from './ai-chat';
import { DocsHeader } from './site-header';
import { LAST_READ_KEY } from '@/lib/last-read';
import { DocsLayout as GlassLayout, type DocsLayoutProps } from 'fumadocs-ui/layouts/glass';
import { Sidebar, SidebarDrawer, SidebarProvider, useSidebar } from 'fumadocs-ui/layouts/glass/slots/sidebar';

function Navigation() {
  const { collapsed } = useSidebar();
  const ref = useRef<HTMLElement>(null);
  const pathname = usePathname();
  useEffect(() => {
    // Wait for the built-in folder expansion before revealing deep links in the scroll area.
    const timer = setTimeout(() => {
      if (!collapsed) ref.current?.querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest' });
    }, 220);
    return () => clearTimeout(timer);
  }, [pathname, collapsed]);
  return <Sidebar ref={ref} aria-label="Documentation navigation" aria-hidden={collapsed} inert={collapsed}
    data-collapsed={collapsed} className={collapsed
      ? 'docs-sidebar w-[296px] -translate-x-[304px]'
      : 'docs-sidebar md:layout:[--fd-left-width:304px]'} />;
}

function MobileNavigation() {
  return <SidebarDrawer contentProps={{ className: 'docs-sidebar', role: 'navigation', 'aria-label': 'Documentation navigation' }} />;
}

/** Remember the guide the reader was on, so the home chart can flag it. Local to this browser only. */
function useRememberLastRead() {
  const pathname = usePathname();
  useEffect(() => {
    const timer = setTimeout(() => {
      const title = document.querySelector('#nd-page h1')?.textContent?.trim();
      if (!title) return;
      try { localStorage.setItem(LAST_READ_KEY, JSON.stringify({ url: pathname, title })); } catch { /* storage unavailable */ }
    }, 400);
    return () => clearTimeout(timer);
  }, [pathname]);
}

export function DocsLayout(props: DocsLayoutProps) {
  const [open, setOpen] = useState(false);
  useRememberLastRead();
  // GitHub is pinned in the header beside Ask AI, so the sidebar does not repeat it.
  return <GlassLayout {...props} githubUrl={undefined} links={props.links?.map((link) => link.type === 'icon' ? link : { ...link, on: 'nav' })}
    aiChat={{ open, onOpenChange: setOpen, trigger: true, panel: <AIChatPanel onClose={() => setOpen(false)} /> }} slots={{ header: DocsHeader, sidebar: {
    main: Navigation, drawer: MobileNavigation, provider: SidebarProvider, use: useSidebar,
  } }} />;
}
