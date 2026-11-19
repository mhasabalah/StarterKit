import type { Metadata } from 'next';
import { Provider } from '@/components/provider';
import { BackToTop } from '@/components/back-to-top';
import { absoluteUrl, production } from '@/lib/site';
import { socialAlt, socialSize } from '@/lib/brand-meta';
import './global.css';

export const metadata: Metadata = {
  metadataBase: new URL(absoluteUrl('/')),
  title: { default: 'StarterKit — Build with confidence', template: '%s | StarterKit' },
  description: 'The documentation for StarterKit, an ASP.NET Core modular monolith foundation.',
  // The favicon is app/icon.svg; the home-screen icon and social card are generated (lib/brand-images.tsx).
  icons: {
    icon: [{ url: absoluteUrl('/icon.svg'), type: 'image/svg+xml' }],
    apple: [{ url: absoluteUrl('/touch-icon.png'), sizes: '180x180', type: 'image/png' }],
  },
  openGraph: { siteName: 'StarterKit', type: 'website', images: [{ url: absoluteUrl('/social.png'), ...socialSize, alt: socialAlt, type: 'image/png' }] },
  twitter: { card: 'summary_large_image', images: [{ url: absoluteUrl('/social.png'), alt: socialAlt }] },
  robots: { index: production, follow: production },
};
export default function Layout({ children }: { children: React.ReactNode }) {
  return <html lang="en" suppressHydrationWarning><body><Provider>{children}<BackToTop /></Provider></body></html>;
}
