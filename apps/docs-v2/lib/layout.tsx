import type { BaseLayoutProps } from 'fumadocs-ui/layouts/shared';
import { repository } from './site';
import { BrandLogo } from '@/components/brand';

export const baseOptions: BaseLayoutProps = {
  themeSwitch: { mode: 'light-dark-system' },
  nav: { title: <span className="brand-lockup"><BrandLogo /><span className="brand-label">Guide</span></span> },
  githubUrl: `https://github.com/${repository}`,
  links: [
    { text: 'Get started', url: '/getting-started/new-project/' },
    { text: 'Architecture', url: '/architecture/overview/' },
    { text: 'Decisions', url: '/decisions/' },
  ],
};
