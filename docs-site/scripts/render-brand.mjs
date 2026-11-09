import { readFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import assert from 'node:assert/strict';
import { brandTokens } from './check-contrast.mjs';
const root = new URL('../', import.meta.url);
const tokens = await brandTokens();
const palette = new Set(Object.values(tokens).flatMap(Object.values));
// Exported SVGs must use the CSS palette; geometry stays editable, original SVG.
for (const asset of ['src/assets/mark.svg', 'src/assets/logo-light.svg', 'src/assets/logo-dark.svg', 'src/assets/og-image.svg', 'public/favicon.svg']) {
  const source = await readFile(new URL(asset, root), 'utf8');
  for (const [color] of source.matchAll(/#[\da-f]{6}\b/gi)) assert(palette.has(color.toLowerCase()), asset+': '+color+' is not a brand token');
}
await mkdir(new URL('public/', root), { recursive: true });
await sharp(await readFile(new URL('src/assets/og-image.svg', root))).resize(1280, 640).png().toFile(fileURLToPath(new URL('public/og-image.png', root)));
const mark = await readFile(new URL('src/assets/mark.svg', root));
for (const [size, path] of [[128, '../templates/icon.png'], [180, 'public/apple-touch-icon.png']]) {
  const glyph = await sharp(mark).resize(Math.round(size * .78)).png().toBuffer();
  await sharp({ create: { width: size, height: size, channels: 4, background: tokens.dark.bg } })
    .composite([{ input: glyph, gravity: 'centre' }]).png().toFile(fileURLToPath(new URL(path, root)));
  console.log('Rendered '+path+': '+size+'x'+size);
}
console.log('Rendered public/og-image.png: 1280x640');
