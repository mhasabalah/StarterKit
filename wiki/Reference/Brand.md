# StarterKit brand

StarterKit is a production-ready ASP.NET Core 10 modular monolith starter. Its identity reflects separately owned modules that compose into one system.

## Logo and mark

The original mark is an abstract S made from three offset rounded slices. A teal terminal distinguishes the final slice. Keep the geometry intact.

- [Mark](../../docs-site/src/assets/mark.svg): standalone symbol; use on a dark slate tile for app and package icons.
- [Light logo](../../docs-site/src/assets/logo-light.svg): indigo wordmark for light backgrounds.
- [Dark logo](../../docs-site/src/assets/logo-dark.svg): pale violet wordmark for dark backgrounds.
- [Adaptive favicon](../../docs-site/public/favicon.svg): switches colors with the browser color scheme.
- [Social source](../../docs-site/src/assets/og-image.svg) and [rendered image](../../docs-site/public/og-image.png): 1280 by 640 social previews.
- [Package icon](../../templates/icon.png): 128 by 128, with an opaque slate background for light and dark NuGet pages.
- [Apple touch icon](../../docs-site/public/apple-touch-icon.png): 180 by 180.

Leave clear space of at least one slice height around the mark and half the wordmark height around the complete logo. Do not use the full wordmark below 120 pixels wide; use the mark at small sizes. The wordmark uses regular Starter and bold, primary-colored Kit, without a space.

## Palette

The source of truth is [brand.css](../../docs-site/src/styles/brand.css). SVGs are portable exports; `npm run brand` checks their colors against the tokens before rendering the PNGs. Update exports when changing palette tokens. Starlight accent, gray, text and surface variables map to these tokens in both themes.

| Token (`--brand-`) | Light | Dark | Usage |
|---|---|---|---|
| primary | #44318d | #b4a6ff | Links, Kit, primary action |
| primary-low | #eeeafa | #272144 | Active navigation, CTA surface |
| primary-high | #34236e | #ddd5ff | Active navigation text |
| teal | #086c61 | #65d9ca | Accents and focus rings |
| bg | #ffffff | #10141f | Page background |
| surface | #f5f7fb | #181e2c | Cards and navigation |
| heading | #172135 | #f4f7fc | Titles |
| text | #273449 | #e1e7f0 | Body copy |
| muted | #526179 | #aab7cb | Supporting copy |
| border | #d4dce8 | #364156 | Dividers |
| gray-1 | #273449 | #e1e7f0 | Strong neutral |
| gray-2 | #3d4c63 | #c7d1e0 | Secondary neutral |
| gray-3 | #526179 | #aab7cb | Muted neutral |
| gray-4 | #687890 | #7e8ba2 | Decorative neutral |
| gray-5 | #d4dce8 | #364156 | Borders |
| gray-6 | #eaf0f7 | #202838 | Inset surfaces |
| gray-7 | #f5f7fb | #181e2c | Raised surfaces |
| on-primary | #ffffff | #10141f | Primary action text |
| note / note-bg | #44318d / #eeeafa | #b4a6ff / #272144 | Information |
| tip / tip-bg | #086c61 / #e3f5f0 | #65d9ca / #123731 | Helpful guidance |
| caution / caution-bg | #79500b / #fff4d9 | #f4ce83 / #392d19 | Cautions |
| danger / danger-bg | #a12442 / #ffedf1 | #ffa8b5 / #3c202b | Destructive actions and errors |

Run `npm run check:contrast` in `docs-site` to assert WCAG AA (4.5:1) for body copy, supporting copy, links, actions, active navigation and semantic aside colors. Use semantic labels as well as color. Decorative gray-4 is not a body-text color.

## Typography

Use locally bundled Inter for UI and body copy, with weights 400, 500, 600 and 700. Use JetBrains Mono 400 for code. Documentation has a 48rem content measure and a 1.7 line height; splash content has a wider grid. Headings use a restrained scale and tighter spacing. Avoid all-caps paragraphs; short technical eyebrows may use capitals.

SVG wordmarks use Inter with Arial and generic sans-serif fallbacks. The social image uses Arial with a generic sans-serif fallback so Sharp can render it without a remote font. Run `npm run brand` after changing artwork and inspect the generated PNGs before committing them.

## Voice

Be concise, senior and precise. Name the behavior and its constraints. Say “tenant data uses PostgreSQL row-level security,” not “unbreakable security.” Explain production configuration without promising that a template removes deployment decisions.

## Do and don't

- Do keep the mark proportions, clear space and theme-specific wordmark colors.
- Do use the original SVG sources and regenerate PNGs instead of enlarging small raster images.
- Do use readable text, visible focus rings and descriptive links.
- Do state prerequisites beside commands and link to canonical guides.
- Don't stretch, rotate or rearrange individual slices, add effects to the wordmark, or substitute another company's artwork.
- Don't use the .NET purple as the StarterKit primary, place pale artwork on pale backgrounds, or rely on color alone.
- Don't describe optional integrations or environment-specific configuration as automatic guarantees.
