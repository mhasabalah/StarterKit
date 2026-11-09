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
are checked during builds; failures stop publication. Mermaid integration is deferred to T3.

`DOCS_SITE` optionally sets the deployment origin; `DOCS_BASE` sets the URL prefix (default `/`).
`GITHUB_REPOSITORY` sets the repository for source links (default `mhasabalah/StarterKit`). The Pages workflow
supplies the origin and prefix on pushes to master; PR and manual builds produce downloadable artifacts.

Brand tokens live in `src/styles/brand.css`; see [the brand guide](../wiki/Reference/Brand.md).
Run `npm run brand` to render the social, package and Apple icons from the SVG sources, and
`npm run check:contrast` to check both palettes against WCAG AA. Generated PNGs are checked in.
The splash uses `BrandHero.astro` for base-aware actions; its MDX links also use `siteBase`.
