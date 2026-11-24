import { repository } from './site';

/** The guide's top-level destinations, shared by the home header, its phone menu and the Fumadocs layout options. */
export const navLinks = [
  { text: 'Get started', url: '/getting-started/new-project/' },
  { text: 'Architecture', url: '/architecture/overview/' },
  { text: 'Decisions', url: '/decisions/' },
  { text: 'Config builder', url: '/tools/config-builder/' },
];
export const githubUrl = `https://github.com/${repository}`;
