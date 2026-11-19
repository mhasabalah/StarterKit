---
version: 1
slug: "app-home-page-tsx"
primary_target: "app/(home)/page.tsx"
related_targets: ["app/(docs)/[...slug]/page.tsx","app/global.css"]
---

# docs-v2: home and documentation shell

Scope: the docs-v2 home (`app/(home)/page.tsx`) plus the whole reading shell (header, sidebar, docs page header,
prose, code, tables, callouts, TOC, search and AI chat panels, 404). Visitor mode: Read. Build path: code-led.

Audience and job: developers building on StarterKit, mid-task, arriving from search or deep links; they need the
right guide in one or two moves and comfortable long-form reading. Constraints: WCAG 2.2 AA in both themes (axe in
Playwright), no horizontal scroll at 320/390/1440, Fumadocs Glass layout behaviour kept, local fonts only.
Avoid (user): marketing-landing feel, generic template look, dense/busy, plain/forgettable.

## Direction contract

THESIS: The guide opens on a chart of the system: every module an element placed by its real ModuleOrder, the
cross-cutting practices as a detached series. It refuses the category's centred search over a grid of icon cards.

OWN-WORLD: An offset-printed classroom wall chart. Cool chart-white ground (dark: blue-black slate), ink type, hairline
cell rules, four flat family tints used only as cell fills and section markers: Foundations cobalt, Connected green,
Samples ochre, Practices violet. Heavy grotesk symbols and headings, a quiet reading sans, mono for order numerals and
code. Rank by inversion: the current item prints as an ink tile.

STORY: The reader sees the system's shape in one glance, opens the module or practice they need (or searches), and on
every page knows which element they stand in.

FIRST VIEWPORT: Left five columns: title, one-line lead, a full-width ink search action (primary), the template command
with copy. Right seven: the chart; Id 000 top-left, Nt 900 top-right, 100 to 600 across, then Shipping 700, a dashed
"your module" 700 cell and Agentic 800; the practices series below; a legend whose families light their tiles.

FORM: Periodic wall chart, candidate 3 of 7 on the ordered list. seed:027366cd71f9f650

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Raises (from declined challengers): every tile pairs its claim with proof (real order + real page count); the chart
reflows by whole cells; rank by inversion only; the last-read guide is flagged with a folded corner (local only); a
legend family lights all its tiles; strata with deep gaps between dense chart and quiet reading blocks.

Memorable moment: the 700 slot, where Shipping sits beside the dashed cell for the reader's own module.
