import type { Metadata } from 'next';
import catalog from '@/.generated/config-catalog.json';
import { ConfigBuilder } from '@/components/config-builder/config-builder';
import type { Catalog } from '@/components/config-builder/types';
import { configBuilder } from '@/lib/tools.mjs';
import { absoluteUrl } from '@/lib/site';

export const metadata: Metadata = {
  title: configBuilder.title,
  description: configBuilder.description,
  alternates: { canonical: absoluteUrl(configBuilder.url) },
};

/** Static shell; the builder is a client component fed by the catalog generated from the real files at sync. */
export default function Page() {
  return <ConfigBuilder catalog={catalog as unknown as Catalog} />;
}
