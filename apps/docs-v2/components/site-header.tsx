'use client';
import Link from 'next/link';
import { useEffect, useId, useRef, useState, type ComponentProps } from 'react';
import { Menu, MessageCircle, Search, SidebarIcon, X } from 'lucide-react';
import { useSearchContext } from 'fumadocs-ui/contexts/search';
import { useGlassLayout } from 'fumadocs-ui/layouts/glass';
import { useSidebar as useDrawer } from 'fumadocs-ui/components/sidebar/base';
import { ThemeSwitch } from 'fumadocs-ui/layouts/shared/slots/theme-switch';
import { usePathname } from 'fumadocs-core/framework';
import { BrandLogo } from './brand';
import { navLinks, githubUrl } from '@/lib/nav';

/** GitHub's mark, drawn inline (lucide ships no brand icons). */
function GitHubMark() {
  return <svg viewBox="0 0 24 24" aria-hidden="true" fill="currentColor">
    <path d="M12 .5a11.5 11.5 0 0 0-3.64 22.41c.58.1.79-.25.79-.56v-2c-3.2.7-3.88-1.37-3.88-1.37-.53-1.33-1.28-1.69-1.28-1.69-1.05-.71.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.77 2.7 1.26 3.36.96.1-.75.4-1.26.73-1.55-2.55-.29-5.24-1.28-5.24-5.68 0-1.26.45-2.28 1.19-3.09-.12-.29-.52-1.46.11-3.05 0 0 .97-.31 3.17 1.18a11 11 0 0 1 5.78 0c2.2-1.49 3.17-1.18 3.17-1.18.63 1.59.23 2.76.11 3.05.74.81 1.19 1.83 1.19 3.09 0 4.41-2.69 5.38-5.25 5.67.41.36.78 1.06.78 2.14v3.17c0 .31.21.67.8.56A11.5 11.5 0 0 0 12 .5Z" />
  </svg>;
}

export function GitHubLink({ className = 'site-icon' }: { className?: string }) {
  return <a href={githubUrl} className={className} target="_blank" rel="noopener noreferrer" aria-label="StarterKit on GitHub"><GitHubMark /></a>;
}

function SearchIcon({ className = 'site-icon' }: { className?: string }) {
  const { setOpenSearch } = useSearchContext();
  return <button type="button" className={className} aria-label="Search" onClick={() => setOpenSearch(true)}><Search aria-hidden="true" /></button>;
}

function HomeMark() {
  return <Link href="/" className="site-brand" aria-label="StarterKit guide home"><BrandLogo /></Link>;
}

/**
 * The documentation header (Glass layout `header` slot). On wide screens it is the ruled running head: search, Ask AI,
 * GitHub and theme. On phones it becomes a top bar that pins the brand, search, Ask AI, GitHub and the navigation
 * drawer, replacing Glass's floating bottom buttons. It keeps Glass's grid placement and sticky behaviour.
 */
export function DocsHeader({ className, ...props }: ComponentProps<'div'>) {
  const { props: { aiChat }, slots } = useGlassLayout();
  const sidebar = slots.sidebar.use();
  const drawer = useDrawer();
  const Full = slots.searchTrigger ? slots.searchTrigger.full : undefined;
  return <div className={['docs-header sticky flex flex-row justify-end gap-2 [grid-area:left-margin/left-margin/right/right] z-20 px-4 md:top-0 md:pt-2 md:px-2 md:h-(--fd-header-height) md:layout:[--fd-header-height:--spacing(12)] md:bg-linear-to-b md:from-fd-background', className].filter(Boolean).join(' ')} {...props}>
    {sidebar.collapsible && sidebar.collapsed && <button type="button" className="site-icon me-auto max-md:hidden" aria-label="Show Sidebar" onClick={() => sidebar.setCollapsed(false)}><SidebarIcon aria-hidden="true" /></button>}
    {Full && <div className="@container flex justify-end flex-1 max-md:hidden"><Full className="text-fd-muted-foreground ps-3 w-full @sm:max-w-[200px]" /></div>}
    {aiChat && !aiChat.open && <button type="button" className="site-action max-md:hidden" onClick={() => aiChat.onOpenChange(true)}><MessageCircle aria-hidden="true" />Ask AI</button>}
    <GitHubLink className="site-icon max-md:hidden" />
    <ThemeSwitch mode="light-dark-system" className="shrink-0 max-md:hidden" />
    <div className="site-bar md:hidden">
      <HomeMark />
      <div className="site-bar-actions">
        <SearchIcon />
        {aiChat && <button type="button" className="site-icon" aria-label="Ask AI" aria-pressed={aiChat.open} onClick={() => aiChat.onOpenChange(!aiChat.open)}><MessageCircle aria-hidden="true" /></button>}
        <GitHubLink />
        <button type="button" className="site-icon" aria-label="Open navigation menu" aria-expanded={drawer.open} aria-controls="nd-sidebar-mobile" onClick={() => drawer.setOpen(!drawer.open)}><Menu aria-hidden="true" /></button>
      </div>
    </div>
  </div>;
}

/** The home header (HomeLayout `header` slot): the same bar, with the guide's sections as links and a menu on phones. */
export function HomeHeader() {
  const [open, setOpen] = useState(false);
  const panel = useId();
  const ref = useRef<HTMLElement>(null);
  const pathname = usePathname();
  const [openedOn, setOpenedOn] = useState(pathname);
  if (open && openedOn !== pathname) { setOpen(false); setOpenedOn(pathname); } // close after navigation
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false); };
    const onPointer = (event: PointerEvent) => { if (!ref.current?.contains(event.target as Node)) setOpen(false); };
    document.addEventListener('keydown', onKey);
    document.addEventListener('pointerdown', onPointer);
    return () => { document.removeEventListener('keydown', onKey); document.removeEventListener('pointerdown', onPointer); };
  }, [open]);
  return <header id="nd-nav" ref={ref} className="home-header" data-open={open || undefined}>
    <div className="home-header-bar">
      <Link href="/" className="site-brand" aria-label="StarterKit guide home">
        <span className="brand-lockup"><BrandLogo /><span className="brand-label">Guide</span></span>
      </Link>
      <nav aria-label="Guide sections" className="home-header-links">
        {navLinks.map((link) => <Link key={link.url} href={link.url}>{link.text}</Link>)}
      </nav>
      <div className="site-bar-actions">
        <HomeSearch />
        <GitHubLink />
        <ThemeSwitch mode="light-dark-system" className="shrink-0 home-header-theme" />
        <button type="button" className="site-icon home-header-menu" aria-label={open ? 'Close menu' : 'Open menu'} aria-expanded={open} aria-controls={panel} onClick={() => { setOpenedOn(pathname); setOpen(!open); }}>
          {open ? <X aria-hidden="true" /> : <Menu aria-hidden="true" />}
        </button>
      </div>
    </div>
    <div id={panel} className="home-menu" hidden={!open}>
      <nav aria-label="Guide sections">{navLinks.map((link) => <Link key={link.url} href={link.url} onClick={() => setOpen(false)}>{link.text}</Link>)}</nav>
      <div className="home-menu-footer"><span>Theme</span><ThemeSwitch mode="light-dark-system" /></div>
    </div>
  </header>;
}

function HomeSearch() {
  const { setOpenSearch, hotKey } = useSearchContext();
  return <>
    <button type="button" className="home-header-search" onClick={() => setOpenSearch(true)}>
      <Search aria-hidden="true" /><span>Search</span>
      <span className="home-header-keys" aria-hidden="true">{hotKey.map((key, i) => <kbd key={i}>{key.display}</kbd>)}</span>
    </button>
    <SearchIcon className="site-icon home-header-search-icon" />
  </>;
}
