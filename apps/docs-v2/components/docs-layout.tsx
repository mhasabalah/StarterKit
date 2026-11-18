'use client';
import { useEffect, useRef, useState } from 'react';
import { usePathname } from 'fumadocs-core/framework';
import { AIChatPanel } from './ai-chat';
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

export function DocsLayout(props: DocsLayoutProps) {
  const [open, setOpen] = useState(false);
  return <GlassLayout {...props} links={props.links?.map((link) => link.type === 'icon' ? link : { ...link, on: 'nav' })}
    aiChat={{ open, onOpenChange: setOpen, trigger: true, panel: <AIChatPanel onClose={() => setOpen(false)} /> }} slots={{ sidebar: {
    main: Navigation, drawer: MobileNavigation, provider: SidebarProvider, use: useSidebar,
  } }} />;
}
