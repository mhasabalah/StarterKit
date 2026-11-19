import type { BaseLayoutProps } from 'fumadocs-ui/layouts/shared';
import { BrandLogo } from '@/components/brand';
import { githubUrl, navLinks } from './nav';

export const baseOptions: BaseLayoutProps = {
  themeSwitch: { mode: 'light-dark-system' },
  nav: { title: <span className="brand-lockup"><BrandLogo /><span className="brand-label">Guide</span></span> },
  githubUrl,
  links: navLinks,
};
