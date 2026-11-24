import catalog from '../../.generated/config-catalog.json';
import { configBuilder } from '../tools.mjs';

/**
 * Which docs pages lead into the configuration builder, derived from the catalog: a page that is the guide of at
 * least `minimum` keys links to the builder at the section it documents most. The configuration reference links to
 * the builder itself. Nothing here is written by hand, so a page gains or loses the link with the wiki.
 */
const minimum = 5;
const reference = '/reference/configuration/';

function build() {
  const counts = new Map<string, Map<string, number>>();
  for (const [key, [, guide]] of Object.entries(catalog.docs as unknown as Record<string, [string, string | null]>)) {
    if (!guide || !guide.startsWith('/')) continue;
    const route = guide.split('#')[0];
    const section = key.split(':')[0];
    const sections = counts.get(route) ?? new Map<string, number>();
    sections.set(section, (sections.get(section) ?? 0) + 1);
    counts.set(route, sections);
  }
  const links = new Map<string, { section?: string; href: string }>([[reference, { href: configBuilder.url }]]);
  for (const [route, sections] of counts) {
    const total = [...sections.values()].reduce((sum, count) => sum + count, 0);
    if (total < minimum || links.has(route)) continue;
    const [section] = [...sections.entries()].sort((a, b) => b[1] - a[1])[0];
    links.set(route, { section, href: `${configBuilder.url}#section=${encodeURIComponent(section)}` });
  }
  return links;
}

const links = build();

/** The builder link for a docs page URL (with or without the trailing slash), if it has one. */
export function configBuilderLinkFor(url: string) {
  return links.get(url.endsWith('/') ? url : `${url}/`);
}
