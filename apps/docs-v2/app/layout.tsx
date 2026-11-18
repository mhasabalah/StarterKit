import type { Metadata } from 'next';
import { Provider } from '@/components/provider';
import { absoluteUrl, production } from '@/lib/site';
import './global.css';

export const metadata: Metadata = {
  metadataBase: new URL(absoluteUrl('/')),
  title: { default: 'StarterKit — Build with confidence', template: '%s | StarterKit' },
  description: 'The documentation for StarterKit, an ASP.NET Core modular monolith foundation.',
  icons: { icon: absoluteUrl('/favicon.svg') },
  openGraph: { siteName: 'StarterKit', type: 'website', images: [absoluteUrl('/og-image.png')] },
  robots: { index: production, follow: production },
};
export default function Layout({ children }: { children: React.ReactNode }) {
  return <html lang="en" suppressHydrationWarning><body><Provider>{children}</Provider></body></html>;
}
