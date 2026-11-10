# 0024. Render the canonical wiki with Starlight and publish through GitHub Pages

The template documentation site renders the canonical wiki through Astro Starlight, with an explicit reader journey and GitHub Pages publication.

## Status

- **Status:** Accepted
- **Date:** 2026-11-10

## Context

The wiki contains architecture rules, module guides, operating procedures and reference inventories, but a folder listing does not provide a reader journey, search or a consistent reading layout. Maintaining a second set of site articles would let behavior and documentation diverge. The repository also generates products, which must remain independent of the template's publishing toolchain.

## Decision

- **Keep one source.** Edit plain GitHub Markdown in `wiki/`; Astro Starlight in `docs-site/` renders it. `docs-site/scripts/sync-wiki.mjs` generates site content before dev and build, leaving only the committed splash page in place. Do not edit generated articles.
- **Render the full template.** Sync evaluates conditional markers with every symbol enabled, selects the active branches and removes directives from the rendered output. Source markers remain intact for `dotnet new`; generated products still receive their selected view.
- **Navigate by reader task.** `docs-site/sidebar.mjs` explicitly places every wiki page in the reader journey rather than mirroring folders. Every new page also belongs in `wiki/INDEX.md`; missing or duplicate sidebar entries fail sync. The ADR template remains last because it is an authoring aid.
- **Use portable diagrams.** Follow [Diagram style](../Reference/Diagram-Style.md): one idea per Mermaid figure, short labels, layouts designed for a 615px column and an italic caption directly below each fence. The site owns theme colors and diagram expansion; sources contain no theme directives or inline colors.
- **Publish through CI.** [.github/workflows/docs.yml](../../.github/workflows/docs.yml) builds pull-request artifacts and publishes pushes to master through GitHub Pages. Pages supplies the origin and base path; documentation does not hard-code a deployment URL. The owner enables Pages with source "GitHub Actions" and uploads `docs-site/public/og-image.png` as the repository social preview.
- **Keep the site template-only.** `docs-site/**` is excluded by both the product definition in `.template.config/template.json` and the pack content glob in `templates/StarterKit.Templates.csproj`. The product definition also excludes the docs workflow and this ADR; its index entries stay inside template-source conditionals.

## Consequences

Engineers change code and its canonical documentation in one PR, while readers get search, light/dark presentation, page outlines and validated links. GitHub still renders the source Markdown. New pages require both index and sidebar placement, and wiki edits require a sync or dev restart to appear locally.

Run these checks from the repository root (Node 22 or newer and installed site dependencies are prerequisites):

```bash
cd docs-site && npm run check:mermaid && npm run check:contrast && npm run build
```

Run `bash scripts/check-docs.sh` from the repository root as well. The site build validates internal links and anchors; the standalone Mermaid and contrast checks cover diagram parsing and brand palettes. The docs workflow runs the wiki check and site build; run the standalone checks locally before review. The .NET product does not need Node to build or run.

## Alternatives considered

- **Keep only a folder-oriented wiki:** preserves portability but leaves navigation, search and presentation unresolved.
- **Write separate site articles:** creates competing sources for the same behavior.
- **Move articles to MDX or site-specific syntax:** couples the canonical documentation to the publishing framework and weakens GitHub readability.
- **Ship the site in generated products:** adds template branding, dependencies and publishing configuration to every product without a product requirement.

## Related

[ADR index](README.md), [Templates](../Getting-Started/Templates.md), [Diagram style](../Reference/Diagram-Style.md), [Brand](../Reference/Brand.md), [Site maintenance](../../docs-site/README.md), and `/starterkit:update-docs`.
