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

## Documentation V2 decision addendum

### Status

Accepted for independent implementation. Production promotion is pending a separate owner decision. The original
Starlight decision and deployment remain valid for V1.

### Context and problem

The documentation needs a React-based presentation layer, generated API reference, portable Markdown access for
agents, and a broader validation pipeline. The canonical wiki must continue to work in GitHub and generated
products. Replacing the currently deployed application before verification would make rollback unnecessarily hard.

### Considered options

- Extend Starlight: lowest migration cost, but does not meet the selected Next.js/Fumadocs architecture.
- Move the wiki into a new MDX collection: flexible authoring, but introduces competing technical sources.
- Add a separate renderer over the shared wiki: preserves source ownership and permits independent validation.

### Decision

Use [Documentation V2](../../apps/docs-v2/package.json), an isolated npm application in `apps/docs-v2`, with Next.js
App Router, TypeScript, Fumadocs, Tailwind, Shiki, and Mermaid. Keep `wiki/` canonical. V2 reads V1's explicit navigation
metadata as migration input; it does not run V1's generator, import Astro components, or change its deployment.
Both site directories remain excluded from generated .NET products and the template package.

Use the current Fumadocs built-in ZBSearch engine. Earlier planning named Orama; the selected stable Fumadocs release
uses ZBSearch by default. There is no hosted search account. Use official Fumadocs OpenAPI rendering over a local
schema snapshot exported from the actual API. Keep the API playground disabled so documentation never sends live
mutations. Use server rendering and static generation where supported; Mermaid and reader controls are client components.

### Consequences

Use the official Glass layout and page components, with a 296px desktop sidebar and matching collapse offset.
Folders start collapsed and open for the active page. The mobile drawer, theme controls, page actions and search
dialog use Fumadocs components. Sync generates a separate search index from article content and the OpenAPI
snapshot, so search requests do not load the compiled MDX page modules. Container callouts preserve colons in
ordinary text, including times.

The Glass sidebar keeps the framework's tree matching, collapsible folders and mobile drawer. V2 normalizes
navigation URLs to Fumadocs' slashless tree format while Next.js retains the existing trailing-slash public URLs.
Stable node IDs, section icons, nested guide lines and a current-page marker make location visible. The active
branch opens automatically, deep links scroll into view, and reduced-motion settings disable navigation animations.

The V2 homepage provides starting paths for creating a product, understanding its architecture and adding a module.
Its quickstart uses the official Fumadocs server-highlighted code block and copy control. Capability links are grouped
by product foundations, connected systems and engineering operations; their destinations remain canonical wiki pages.
Header and footer branding use the approved theme-specific SVG wordmarks and the adaptive favicon from V1,
following [the brand guidelines](../Reference/Brand.md). The vectors remain regenerated assets, not a second logo source.

Search filters use the official tag controls and filter the server index: all pages, documentation, getting
started, architecture, and API reference. The Glass header's Ask AI button opens a conversation panel with source
links, cancellation, and conversation reset. It reuses documentation search to select bounded excerpts; no vector
database or external document crawler is needed. Configure `DOCS_AI_BASE_URL` (including `/v1`), `DOCS_AI_MODEL`,
and, for authenticated providers, `DOCS_AI_API_KEY` in the deployment environment or an ignored `.env.local`.
The provider must implement OpenAI-compatible chat completions. Keys stay on the server. Without configuration,
the panel explains that the provider is not connected; it never substitutes search results for generated answers.
The last ten messages and up to four documentation excerpts are sent to the configured provider. Review the
provider's data policy and apply deployment-level access and usage limits before public enablement.

V1 and V2 share technical corrections without maintaining two article collections. V2 has its own dependencies,
lockfile, validation workflow, hosting instructions, and rollback boundary. A Node server is required for search;
V2's standalone output is not a GitHub Pages artifact. Optional MCP, hosted search, authentication, additional
languages, and public release-version selectors are outside this implementation.

## V2 technical audit and migration contract

V2's shared Mermaid theme uses the site's purple and teal accents with semantic role classes. Canonical sources
follow [Diagram style](../Reference/Diagram-Style.md); `quality/diagram-review.json` records their review and source
evidence. The renderer and checker use the same configuration. The checker loads the bundled Inter fonts, renders
both themes and enforces the documented width budget. V1's application remains independent; source labels and
relationship annotations carry the meaning when another renderer does not use the V2 semantic colors.

The initial inventory contains 108 published wiki articles, the custom Starlight homepage, 103 Mermaid fences, and
one wiki JSON download. V1's public favicon, social preview and Apple icon are copied; its logo SVGs are also
available. `wiki/INDEX.md` is an authoring index, represented by the homepage and navigation rather than a second
published index, matching V1. There is no documentation localization configuration.

V1 uses Astro 7.4.2 and Starlight 0.42.7. Its sync step selects the full-template branches, derives titles from H1s,
rewrites relative links, and applies Git update dates. V2 preserves those behaviors through its own generator.
Markdown articles remain Markdown; the source parser preserves code fences and recognizes GitHub tables.
The homepage is rebuilt with React using the existing product claims, installation commands, and capability links.

The generated `.generated/inventory.json` records every source, old/new path, source and output hashes, headings,
code hashes, assets, dependencies, conversion status, redirects, and unresolved differences. The same directory
contains navigation metadata, diagram sources and the external-link list. These are build artifacts, uploaded by
CI; regenerate them with `npm run sync` rather than editing them. Content coverage checks fail for missing or
duplicate wiki pages. Code samples and diagrams are compared exactly against the canonical input.

All existing article paths are preserved, including `README.md` mappings and lowercase names. A compatibility
anchor retains the homepage's former numbered "build your first module" target. Unescaped pipe characters inside
table code spans are escaped during generation so the entire code span remains in one cell. Generic type names
mistaken for HTML tags are emitted as text. These conversions do not modify the wiki source.

Unsupported syntax must fail validation or be recorded explicitly. Existing prose formatting exceptions are
listed by file, rule, and line-content hash in `quality/markdown-baseline.json`. New violations fail. Do not refresh
the baseline merely to make CI green. CSpell's project dictionary covers technical names; two file-scoped exceptions
retain existing code/provider spellings. Vale blocks unfinished placeholder prose and offers optional clarity suggestions.

## V2 development and validation

Use Node 24 and npm. From the repository root:

```sh
cd apps/docs-v2
npm ci
npm run dev
```

The development command generates content and watches `wiki/`. Restart after navigation or environment changes.
Do not edit generated `content/docs/`. Fumadocs components are registered in `mdx-components.tsx`: callouts, tabs,
steps, cards, accordions, file trees and code groups. Markdown Mermaid fences need no imports. Diagram controls
provide source copy, SVG download, expansion, zoom and scrolling. Escape closes the dialog and restores focus.
Malformed diagrams display an error and their original source. Mermaid uses strict security and unique render IDs.

From the V2 directory, run:

```sh
npm run check
npm run check:vale
npm run check:external
npx playwright install chromium
npm run check:mermaid
npm run build
npm run test:browser
npm run start
```

With the production server running, use a second terminal to run `npm run check:links`. The browser suite checks
navigation, search, themes, diagram controls, mobile layout, images, AI routes, redirects, and every migrated page.
The diagram checker also covers flowchart, sequence, class, entity relationship, state, journey, Gantt and C4 syntax.
Use `PLAYWRIGHT_CHANNEL=chrome` only when intentionally testing an installed Chrome rather than Playwright's pinned browser.

Install Vale 3.27.0 and Lychee 0.24.2 from their official releases and place their executables on PATH. CI downloads
these pinned releases and verifies their published checksums. Lychee retries transient failures; placeholder hosts
and repository links requiring authentication are explicit exclusions. Internal pages and fragments are checked
against the running production site, independently of external network availability.

The independent [V2 workflow](../../.github/workflows/docs-v2.yml) runs validation and uploads the migration inventory
and browser reports. It does not publish or alter V1. Documentation changes, including site application code, use
the documentation checks above without building or starting the API. Template packaging is checked independently.
Backend/.NET changes require the repository code checks:

```sh
dotnet build StarterKit.slnx
dotnet test src/Api.Tests
```

The docs workflow verifies the OpenAPI snapshot when backend files change, or when explicitly requested with its
`verify_api` workflow-dispatch input. Run `bash scripts/check-docs.sh` for documentation changes.

## V2 API contracts and AI access

The backend exposes `/openapi/v1.json` in Development. After building the solution, run from V2:

```sh
npm run export:openapi
npm run export:openapi -- --check
```

Docker must be running. The exporter creates a disposable Postgres container, boots the real API against that
database with Redis disabled and in-memory messaging, retrieves the schema, and cleans up its owned processes
and container. It does not connect to an existing deployment. Schema object keys are sorted and the server URL
is normalized to `https://api.example.com`; endpoint contracts are otherwise preserved. Review the snapshot diff
before including an export in a change. CI repeats the export and fails on drift.

The initial snapshot has 288 paths. Operations are grouped by schema tags below `/api-reference/v1/`; request,
response, security and validation details come from the schema. Missing summaries, examples, or endpoint-specific
security metadata remain contract limitations rather than invented descriptions. Generated example payloads are
illustrative. The downloadable schema is at `/api-reference/v1/openapi.json`.

Public AI access includes `/llms.txt`, `/llms-full.txt`, and `/llms.mdx/<article-slug>`, plus the homepage at
`/llms.mdx/home`. Articles retain original code and Mermaid fences. API Markdown identifies operations and links
to the complete schema. Copy-page controls use the same routes. Unknown pages return 404. Only published
documentation is exposed; no repository source browsing, credentials, private files, or MCP server is provided.

## V2 deployment, promotion and rollback

Environment variables are documented in [the example](../../apps/docs-v2/.env.example):

| Variable | Meaning |
| --- | --- |
| `DOCS_SITE` | Public origin used for canonical URLs, sitemap and social metadata; required for production publication |
| `DOCS_PRODUCTION` | Set to `true` only for the approved public deployment; otherwise robots and response headers use noindex |
| `NEXT_PUBLIC_DOCS_BASE` | Optional build-time URL prefix such as `/preview`; rebuild when it changes |
| `GITHUB_REPOSITORY` | Repository slug for source links, supplied automatically by GitHub Actions |
| `DOCS_BIND_HOST` | Local standalone server bind host; defaults to localhost |
| `PORT` | Standalone server port; defaults to 3000 |

For Vercel, select `apps/docs-v2` as the project root, enable access to source files outside that directory,
use `npm ci` and `npm run build`, and configure the environment above. Git history must be available for source
dates; shallow exports may omit dates. Do not connect automatic production promotion until review is complete.

For a self-hosted Node deployment, build from the repository checkout and run `npm run start` in V2. It prepares
the standalone output's static assets before starting the server. Terminate TLS at the reverse proxy and forward
the configured prefix without stripping it. A container alternative uses the repository as build context:

```sh
docker build -f apps/docs-v2/Dockerfile -t starterkit-docs-v2 .
docker run --rm -p 127.0.0.1:3000:3000 starterkit-docs-v2
```

Pass production origin, base path and production status as build arguments for a release image. Defaults deliberately
produce an unindexed preview. The runtime image runs as a non-root user. No backend connection is needed to serve docs.

Release checklist:

1. Verify inventory coverage, production build, quality checks, browser reports and OpenAPI drift check.
2. Review all conversion exceptions and contract metadata limitations.
3. Preview both themes, narrow screens, keyboard navigation, search, diagrams and Markdown access.
4. Build with the intended origin and prefix; verify sitemap, canonical URLs, assets and old inbound links.
5. Retain the previous V1 hosting configuration and the V2 image/build identifier.
6. Obtain the owner's production-promotion decision, then switch the documentation domain or reverse-proxy target.
7. Check representative routes and search after switching. Roll back by restoring the previous hosting target.

V1 remains independently runnable and deployed until a future removal decision. Future documentation versions
should correspond to product releases, with pinned contract snapshots and explicit redirect policy. V2 is a website
generation, not a claim that the backend API version has changed.
