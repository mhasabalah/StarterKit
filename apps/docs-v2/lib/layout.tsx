import type { BaseLayoutProps } from 'fumadocs-ui/layouts/shared';
import { publicPath, repository } from './site';
import { BrandLogo } from '@/components/brand';

export const baseOptions: BaseLayoutProps = {
  themeSwitch: { mode: 'light-dark-system' },
  nav: { title: <span className="brand-lockup"><BrandLogo /><span className="brand-label">DOCS</span></span> },
  githubUrl: `https://github.com/${repository}`,
  links: [{ text: 'Start building', url: '/getting-started/new-project/' }, { text: 'Architecture', url: '/architecture/overview/' }],
};
export const brandIcon = publicPath('/favicon.svg');
