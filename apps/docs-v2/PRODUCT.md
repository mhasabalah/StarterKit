# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Primary: developers building a product on StarterKit. They have generated a solution from the `dotnet new starterkit`
template, or are about to, and come to the documentation mid-task: find the guide for a module, a rule, a command or
a deployment step, read it, and get back to their editor. Many arrive from search or a deep link rather than the home
page; the home page is their map and launchpad.

Home page, equally primary: developers and tech leads evaluating the template. They arrive at the home page asking
what StarterKit is built on, how it is structured, what it already does, and why they should start from it instead of
from scratch; the home answers that with facts (the stack, the architecture in three rules, what ships on day one),
each linked to its guide, before the paths into the guide.

Secondary (not designed for first): AI coding agents reading `llms.txt` / `llms-full.txt` and per-page Markdown.

## Product Purpose

docs-v2 is an independent Next.js + Fumadocs renderer of the StarterKit `wiki/` (the same content as the Astro
Starlight site, which stays as V1). It exists to make the StarterKit developer guide fast to navigate and pleasant to
read: modules, multi-tenancy, messaging, identity, deployment, testing, architecture decisions (ADRs) and the API
reference generated from OpenAPI. Success: a developer reaches the right page in one or two moves and can read long
technical pages without fatigue.

## Positioning

StarterKit is a modular monolith on ASP.NET Core 10: one host, separately owned modules that compose into one system,
with boundaries enforced by architecture tests rather than convention. The documentation is the reasoning behind
that system written beside the implementation (rules, guardrails, ADRs), not a marketing site for it. The home page
does make the case for using it, but only with checkable facts: real packages, real modules, real rules, each one a link.

## Operating Context

- Content is synced from `wiki/` into `content/docs` (about 110 pages) by `scripts/sync-content.mjs`; pages are
  Markdown with Mermaid diagrams, callouts, tables and code.
- Features: full-text search dialog, a docs AI chat panel, copy-as-Markdown per page, OpenAPI reference pages,
  `llms.txt`, sitemap, light and dark themes.
- Readers move between the docs and their terminal or IDE; commands on a page get copied and run.
- Deployed under an optional base path (`NEXT_PUBLIC_DOCS_BASE`); the public host is per deployment.

## Capabilities and Constraints

- Stack: Next.js 16 (webpack build), React 19, Fumadocs UI/Core 16 (Glass docs layout), Tailwind CSS 4,
  lucide-react icons, locally bundled fonts via `@fontsource` (no hosted font stylesheets).
- Browser tests (`tests/browser/*.spec.ts`, Playwright + axe) assert WCAG 2.2 AA with no violations, no horizontal
  scroll at 320/390/1440 px, both themes, and specific home-page structure (three paths, twelve guides, the template
  command block with copy, the generated-project group). Structural changes must update those tests deliberately.
- V1 (`docs-site/`, Astro Starlight) must be preserved and is checked separately.
- No real hostnames, credentials or IPs in committed files (`api.example.com` placeholders).

## Brand Commitments

- Name: StarterKit (wordmark "Starter" regular + "Kit" bold, no space).
- Existing mark: an abstract S of three offset rounded slices with a distinct terminal slice
  (`public/logo-light.svg`, `logo-dark.svg`, `favicon.svg`; brand page `wiki/Reference/Brand.md`).
- For docs-v2 the user declared the visual world fully open (palette, type and logo are replaceable). Any change to
  shared brand assets must be reflected in `wiki/Reference/Brand.md`.
- Voice (binding, from the brand page): concise, senior and precise. Name the behavior and its constraints; no hype,
  no promises that a template removes deployment decisions.

## Evidence on Hand

- Real content: the full wiki (module guides, 29 ADRs, deployment and testing guides), the OpenAPI spec
  (`openapi/v1.json`), the template commands (`dotnet new install StarterKit.Templates`,
  `dotnet new starterkit -n Acme`), and the generated solution layout (Api, Modules, Shared, Aspire).
- Absent and never to be fabricated: users, customers, testimonials, download counts, stars, benchmarks, pricing.

## Product Principles

1. The reader is mid-task: every screen shortens the path to the right page.
2. Show the system, not adjectives: real commands, real structure, real rules.
3. Long-form reading comfort beats decoration on every docs page.
4. Calm but characterful: neither an ad nor a generic docs template.
5. Accessible in both themes by default; AA is the floor, tested.

## Accessibility & Inclusion

WCAG 2.2 AA, verified by axe in light and dark at mobile and desktop widths; visible focus rings; reduced-motion
respected; no information carried by color alone.
