# StarterKit documentation site

Astro + Starlight publish the existing `wiki/` Markdown. Run these commands from `docs-site/` with Node 22:

```sh
npm ci
npm run dev
npm run build
```

Both dev and build run `npm run sync` first. Sync validates the explicit inventory in `sidebar.mjs`, renders
template conditionals with every symbol enabled, rewrites links, copies assets, and replaces generated content
under `src/content/docs/`. Only the committed `index.mdx` splash is preserved. Edit wiki sources, then rerun sync
or restart dev. Source H1s provide page titles; sidebar entries control navigation labels and order. Edit links
and last-updated dates point back to the original wiki files and their Git history.

To add a page, add its wiki Markdown, link it in `wiki/INDEX.md`, and place it in `sidebar.mjs`. Missing or duplicate
entries fail sync. The ADR template is last in Decisions because it is an authoring aid. Internal links and anchors
are checked during builds; failures stop publication.

Mermaid runs in the browser through `astro-mermaid`, registered before Starlight. `scripts/mermaid-brand.mjs`
derives the base theme variables from `brand.css`. Its SVG CSS uses live brand tokens so the light/dark toggle
updates all five supported diagram forms without re-rendering. Stock `autoTheme` is disabled because it replaces
the base theme with Mermaid's default/dark themes. Figures scale to the content column with `useMaxWidth` and
`max-width: 100%`; source layouts target a natural width of at most 760px. Sequence labels wrap without changing
font size. Italic `Figure:` paragraphs are styled as captions.

`DiagramHead.astro` loads `diagram-expand.js` on every page. Each rendered SVG gets an accessible Expand button.
The native dialog displays it at natural size with scrolling, branded colors, Escape/backdrop dismissal and focus
restoration. Moving the SVG preserves unique IDs and its theme CSS. No sync changes are needed. Authoring
conventions live in [Diagram style](../wiki/Reference/Diagram-Style.md).

Run `npm run check:mermaid` to parse every Mermaid fence in `wiki/` and the root README offline, including both
template branches. It uses Mermaid's browser parser with the Chromium already installed for `mmdc`; it never
downloads a browser or accesses external URLs. Failures identify the source file and opening-fence line.
In a restricted runner that cannot start Chromium's sandbox, set `MERMAID_NO_SANDBOX=1` for this local-file-only
check. Normal machines should leave it unset.

`DOCS_SITE` optionally sets the deployment origin; `DOCS_BASE` sets the URL prefix (default `/`).
`GITHUB_REPOSITORY` sets the repository for source links (default `mhasabalah/StarterKit`). The Pages workflow
supplies the origin and prefix on pushes to master; PR and manual builds produce downloadable artifacts.

Brand tokens live in `src/styles/brand.css`; see [the brand guide](../wiki/Reference/Brand.md).
Run `npm run brand` to render the social, package and Apple icons from the SVG sources, and
`npm run check:contrast` to check both palettes against WCAG AA. Generated PNGs are checked in.
The splash uses `BrandHero.astro` for base-aware actions; its MDX links also use `siteBase`.
