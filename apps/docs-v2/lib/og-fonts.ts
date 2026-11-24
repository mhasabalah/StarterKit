import { readFile } from 'node:fs/promises';
import path from 'node:path';

/** Self-hosted faces for generated images (Satori reads WOFF, not WOFF2); read once at build time. */
const file = (pkg: string, name: string) => readFile(path.join(process.cwd(), 'node_modules/@fontsource', pkg, 'files', name));

export async function ogFonts() {
  const [display, displayHeavy, sans, mono] = await Promise.all([
    file('schibsted-grotesk', 'schibsted-grotesk-latin-500-normal.woff'),
    file('schibsted-grotesk', 'schibsted-grotesk-latin-800-normal.woff'),
    file('ibm-plex-sans', 'ibm-plex-sans-latin-500-normal.woff'),
    file('jetbrains-mono', 'jetbrains-mono-latin-500-normal.woff'),
  ]);
  return [
    { name: 'Schibsted Grotesk', data: display, weight: 500 as const, style: 'normal' as const },
    { name: 'Schibsted Grotesk', data: displayHeavy, weight: 800 as const, style: 'normal' as const },
    { name: 'IBM Plex Sans', data: sans, weight: 500 as const, style: 'normal' as const },
    { name: 'JetBrains Mono', data: mono, weight: 500 as const, style: 'normal' as const },
  ];
}
