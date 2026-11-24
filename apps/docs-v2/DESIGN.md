---
name: StarterKit developer guide (docs-v2)
description: The module chart. An offset-printed wall chart of the system, framing a calm reading column.
colors:
  ground: "#f3f4f7"
  paper: "#ffffff"
  ink: "#0f141d"
  text: "#2b3242"
  muted: "#5a6476"
  rule: "#d8dce4"
  rule-strong: "#b6bdc9"
  hover: "#e9ebf0"
  foundations: "#2342b5"
  foundations-tint: "#e3e8fa"
  connected: "#0a6a49"
  connected-tint: "#dcefe6"
  samples: "#8a4a06"
  samples-tint: "#f8e9d3"
  practices: "#5534b3"
  practices-tint: "#ebe6fa"
  api: "#3d4658"
  api-tint: "#e6e8ed"
  danger: "#a8203d"
  danger-tint: "#f9e3e8"
typography:
  display:
    fontFamily: "Schibsted Grotesk, Inter, ui-sans-serif, sans-serif"
    fontSize: "clamp(2.5rem, 4.4vw, 4rem)"
    fontWeight: 800
    lineHeight: 0.98
    letterSpacing: "-0.04em"
  headline:
    fontFamily: "Schibsted Grotesk, Inter, ui-sans-serif, sans-serif"
    fontSize: "clamp(1.875rem, 3.2vw, 2.5rem)"
    fontWeight: 800
    lineHeight: 1.08
    letterSpacing: "-0.035em"
  title:
    fontFamily: "Schibsted Grotesk, Inter, ui-sans-serif, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "-0.02em"
  symbol:
    fontFamily: "Schibsted Grotesk, Inter, ui-sans-serif, sans-serif"
    fontSize: "clamp(1.75rem, 2.6vw, 2.5rem)"
    fontWeight: 800
    lineHeight: 0.9
    letterSpacing: "-0.045em"
  body:
    fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.72
    fontFeature: "'cv11', 'ss01'"
  label:
    fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 500
    lineHeight: 1
  numeral:
    fontFamily: "JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.6875rem"
    fontWeight: 500
    lineHeight: 1
    fontFeature: "'tnum'"
  code:
    fontFamily: "JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.75
rounded:
  cell: "2px"
  chip: "3px"
  sm: "4px"
  md: "6px"
  lg: "8px"
spacing:
  cell-gap: "1px"
  gutter: "clamp(16px, 4vw, 48px)"
  stratum: "clamp(56px, 7vw, 96px)"
  reading: "70ch"
components:
  search-trigger:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "0 12px 0 18px"
    height: "56px"
  tile:
    backgroundColor: "{colors.foundations-tint}"
    textColor: "{colors.foundations}"
    typography: "{typography.symbol}"
    rounded: "{rounded.cell}"
    padding: "10px 11px 11px"
  tile-hover:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
  tile-yours:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    rounded: "{rounded.cell}"
  element-tile:
    backgroundColor: "{colors.foundations-tint}"
    textColor: "{colors.foundations}"
    rounded: "{rounded.sm}"
    padding: "6px 7px"
    width: "64px"
    height: "60px"
  element-chip:
    backgroundColor: "{colors.foundations-tint}"
    textColor: "{colors.foundations}"
    rounded: "{rounded.chip}"
    width: "22px"
    height: "20px"
  legend-button:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.sm}"
    padding: "0 12px 0 10px"
    height: "34px"
  legend-button-pressed:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
  sidebar-link:
    textColor: "{colors.text}"
    rounded: "{rounded.sm}"
    height: "34px"
  sidebar-link-current:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
  page-tool-button:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.chip}"
    padding: "0 10px"
    height: "30px"
  chat-submit:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.sm}"
    padding: ".45rem .75rem"
---

# Design System: StarterKit developer guide (docs-v2)

## Overview

**Creative North Star: "The Module Chart"**

An offset-printed classroom wall chart of the system. Every module is an element placed by its real `ModuleOrder`, the cross-cutting practices hang below as a detached series, and every documentation page carries the element it belongs to beside its title. The ground is a cool chart-white (dark: blue-black slate), type is ink, cells are separated by printed hairlines, and four flat family tints fill cells and mark sections. Nothing is glossy, gradient-lit or floating; the chart is printed, and the reading column it frames is calm.

Density is deliberately split into strata. The chart is dense (cells touching through a 1px gap); the strata around it are quiet, separated by deep gaps and single rules. Long-form pages drop the chart down to a single tile in the page head and a symbol chip per sidebar entry, so the reader always knows which element they stand in without the page competing with its own text.

Every claim on a cell is proof: the top corner shows the real startup order and the real page count. Rank is shown only by inversion: the current, pressed or hovered item prints as an ink tile.

**Key Characteristics:**
- Cool chart-white ground, ink type, hairline rules; no decorative imagery.
- Four flat family tints (Foundations cobalt, Connected green, Samples ochre, Practices violet) plus a neutral slate family for non-chart groups and the API reference.
- Heavy grotesk symbols and headings, a quiet reading sans, mono for order numerals and code.
- Rank by inversion: the selected item becomes an ink tile with paper text.
- Ruled structure (2px ink header rule, 1px hairline rows) instead of boxed cards.

## Colors

A printed palette: one cool neutral ramp for paper and ink, and four flat inks for the families, each paired with its own pale tint.

### Primary
- **Cobalt Foundations** (foundations): the Foundations family ink (Identity, Tenancy, Attachment, Subscriptions), and the system's interactive accent: focus outline, caret, search-input focus rule, prose link underline and link hover, search match highlight, selection tint (foundations-tint).

### Secondary
- **Chart Green** (connected): the Connected family (Webhooks, DataTransfer, Agentic, Notification); also the terminal slice of the brand mark.
- **Press Ochre** (samples): the Samples family (Catalog, Shipping).

### Tertiary
- **Practice Violet** (practices): the Practices series (Start here, Architecture, Deployment...), and ADR numerals on the home page.
- **Slate** (api): the neutral family for the API reference tile and sidebar groups that are not chart elements (Concepts, Modules, Operations).
- **Signal Red** (danger): status only, never a family. Error callouts, the Ask AI error line and the DELETE method chip.

Each family colour has a tint (`*-tint`) used as the cell fill; the family colour itself is used for the cell's symbol and numerals, never as a large fill.

### Neutral
- **Chart White** (ground): home and 404 page ground, sidebar, title strips of code blocks and diagrams, inline code background.
- **Paper** (paper): docs reading surface, buttons at rest, dialogs and panels.
- **Ink** (ink): headings, the inverted rank tile, primary actions, 2px header rules.
- **Body Text** (text): running text on docs pages and sidebar links.
- **Muted** (muted): leads, captions, TOC, footer, inactive chrome.
- **Hairline** (rule) and **Strong Hairline** (rule-strong): cell and row rules, borders; the strong rule for dashed empty cells, hover borders and link underlines at rest.
- **Hover Wash** (hover): hover fill for sidebar entries and toolbar buttons.

Dark theme redefines every token under `.dark` (ground #0b0e14, paper #10141c, ink #eef1f6, lightened family inks over deep tints; values in the sidecar). Components reference the role, never the theme value.

### Named Rules
**The Tint-Fill Rule.** Family colours appear as cell fills (their tint) and small markers (legend swatch, chip, numerals) only. A family never paints a page background, a heading or a button.

**The One Accent Rule.** Cobalt is the only interactive accent. Focus, caret, prose links and search highlights use it; every other emphasis is ink.

## Typography

**Display Font:** Schibsted Grotesk 600/700/800 (with Inter, ui-sans-serif)
**Body Font:** Inter 400-700 with `cv11` and `ss01` (with ui-sans-serif, system-ui)
**Label/Mono Font:** JetBrains Mono 400/500 (with ui-monospace)

**Character:** A heavy, tightly tracked grotesk prints the chart's element symbols and every heading; Inter reads quietly underneath; mono is reserved for numbers that are data (orders, page counts, ADR numbers) and code. All three are bundled locally via `@fontsource`.

### Hierarchy
- **Display** (800, clamp 2.5-4rem, 0.98, -0.04em): the home title only.
- **Headline** (800, clamp 1.875-2.5rem, 1.08, -0.035em): the docs page title beside its element tile; 404 title (clamp 2-2.75rem). Stratum headings on the home use the same voice at clamp 1.5-2rem.
- **Title** (800, 1.5rem, 1.2): prose H2, which also opens with a hairline rule above it. H3 is 700 at 1.1875rem, H4 700 at 1rem.
- **Symbol** (800, clamp 1.75-2.5rem, 0.9, -0.045em): two-letter element symbols in chart tiles; 1.5rem in the page-head tile, 0.6875rem/700 in sidebar chips.
- **Body** (400, 1rem, 1.72): prose, capped at a 70ch reading measure. Home leads are 1.0625rem/1.65 in muted.
- **Label** (500-600, 0.75-0.875rem): buttons, legend, sidebar (0.8125rem), TOC, table headers (600, 0.8125rem).
- **Numeral** (mono 500, 0.625-0.75rem, tabular): tile corners, ADR numbers. Code blocks are mono 0.8125rem/1.75.

### Named Rules
**The Data-Is-Mono Rule.** Mono marks only numbers that are facts (startup order, page counts, ADR ids) and code. It is never a stylistic label.

**The Heavy Headings Rule.** Every heading is the display grotesk at 700-800 with negative tracking and balanced wrapping; body copy never borrows it.

## Layout

The home is a 1360px max-width page with a fluid gutter (clamp 16-48px). The hero is a 5:8 two-column grid: a sticky intro column (title, lead, ink search action, last-read link, template command) beside the chart. Below 1100px it collapses to one column and the intro stops sticking.

The chart is a 6-column grid with a 1px gap, so neighbouring cell outlines meet as one printed hairline. Modules are placed by order: Identity 000 top-left, Notification 900 top-right, the chart key spanning columns 2-5 between them, 100-600 across the second row, then Shipping 700, the dashed "your module" 700 cell and Agentic 800. The practices series is a separate 4-column ruled block below; then the legend. At 760px the chart reflows by whole cells to 3 columns (practices to 2), the key moves above.

Strata below the hero (the why showcase band; three ways in; the request journey; guardrails and decisions; the colophon footer) are separated by a 1px rule and a deep vertical gap (clamp 56-96px). Each opens on a split head (heading in five columns, a one-line purpose in muted on the right, collapsing to one column at 760px), and its content hangs from a 2px ink rule.

The docs shell keeps the Fumadocs Glass layout: a flush sidebar on the ground colour with a 1px right rule (no floating card from 768px), a ruled sticky running head (search, Ask AI, theme), the reading column at 70ch, and a labelled, ruled outline in the right gutter from 1280px. Touch targets in the sidebar grow to 42px below 768px.

**The Whole-Cell Reflow Rule.** The chart never squeezes, scrolls or crops a cell; it reflows by whole cells and keeps the order sequence readable.

**The Strata Rule.** Dense blocks (the chart, tables) alternate with quiet reading blocks across deep gaps; never stack two dense blocks without a rule and a stratum gap between them.

## Elevation & Depth

Flat and printed. Depth comes from inversion (ink on paper) and hairlines, not shadows. Cells use a 1px outline drawn as a ring shadow so neighbours merge into one rule. Real shadows are reserved for the two things that genuinely sit above the page: the home search action and the search dialog. The sticky running head uses a translucent paper fill with an 8px blur.

### Shadow Vocabulary
- **Cell rule** (`box-shadow: 0 0 0 1px color-mix(in srgb, var(--sk-ink) 16%, transparent)`; 14% on the page-head tile): the printed cell outline.
- **Action lift** (`0 1px 2px ink 18%, 0 8px 24px -12px ink 45%`; hover deepens to `0 14px 30px -14px ink 60%` with a 1px rise): the home search trigger only.
- **Dialog** (`0 24px 60px -24px ink 45%, 0 2px 6px ink 8%`): the search dialog.

### Named Rules
**The Printed Surface Rule.** Cards, tiles, code blocks, callouts, tables and diagrams carry no drop shadow. A new surface earns depth only if it overlays the page.

## Shapes

Nearly square. Chart cells are 2px; chips and page tools 3px; buttons, sidebar entries, legend and options 4px; code frames, diagrams, search action and textareas 6px; dialogs 8px. Borders are 1px hairlines; header rules are 2px ink. The one dashed form is the empty cell (1.5px dashed strong hairline): the "your module" 700 slot and the 404 cell. A last-read guide is flagged by an 18px folded corner, a diagonal split of the ground colour over a deepened family tint.

## Components

### Buttons
- **Shape:** gently squared (4px; 6px for the home search action).
- **Primary (ink):** the home search trigger is a full-width 56px ink bar with paper text, a search icon and paper-on-ink keycaps; the chat submit button is ink when enabled. Only one primary per view.
- **Secondary:** paper fill, 1px hairline border, ink label (legend buttons 34px, page tools 30px, chat buttons). Hover strengthens the border (rule-strong, or ink for page tools).
- **Pressed / selected:** inverts to ink with paper text.
- **Focus:** 2px cobalt outline, 3px offset (inset -2px in the sidebar).

### Chips
- **Element chip:** 22x20px, 3px radius, family tint fill with the family-coloured two-letter symbol (display 700, 0.6875rem). Precedes every top-level sidebar entry and module entry; non-chart groups use the slate family. On the current page it turns paper-on-ink with a translucent paper fill.

- **Method chip:** HTTP methods in the API reference print as 2px-radius tint chips: GET connected, POST foundations, PUT and PATCH samples, DELETE danger.
- **Citation chip:** a `[n]` citation in an Ask AI answer becomes a small cobalt-tint numbered tile linking to source n; it inverts to ink on hover or focus. A citation without a matching source stays text.
- **Source chip:** Ask AI sources lead with the element chip of the page they cite; API pages take the slate "Ap" chip, pages outside the chart a dashed empty chip.

### Cards / Containers
- **Corner Style:** 6px for code and diagrams.
- **Background:** paper body, ground title strip.
- **Shadow Strategy:** none (see Elevation).
- **Border:** 1px hairline; title strips are separated by a hairline.
- **Internal Padding:** diagram canvas clamp(12-24px).
- **Callouts:** a 3px-radius cell filled with 7% of the callout colour over paper, a hairline mixed from the same colour, icon and 600 ink title. No coloured side bar. Info is cobalt, warning amber, success green, idea violet, error signal red.
- **API reference:** operation cards, schema panels and example panels use 3px radii; each section heading sits under a full-width hairline; the selected response status tab inverts to ink.
- **Diagrams:** Mermaid uses the same tints by responsibility (`lib/mermaid-theme.mjs`): ink edges, 2px node radii, 1.25px boundaries, dashed group frames.

### Inputs / Fields
- **Style:** the chat textarea is paper with a 1px hairline and 6px radius; the search field in the running head is ground-filled with a hairline, min 240px wide.
- **Focus:** 2px cobalt outline; inside the search dialog the header shows a 2px cobalt underline instead. While the index loads, a 2px ink scanning rule slides along the dialog header.
- **Disabled:** 50% opacity, default cursor.

- **Ask AI answers:** a Markdown subset (paragraphs, lists, inline and fenced code, tables, quotes, links) in the reading styles at panel scale: hairline-framed ground code, 2px ink table header rule. Headings print as strong lines. It is rendered from a syntax tree to allowlisted elements (`components/chat-markdown.tsx`): HTML prints as text, images become links, only relative, fragment, http and https links are live.

### Copy for Agent
- **Docs pages:** "Copy for agent" leads the page tools as the only ink-filled control (it starts work); it copies the page's Markdown wrapped with what StarterKit is, the rules from `CLAUDE.md` and the guardrail commands, the page's source link and a "My task:" slot.
- **Home:** "Copy prompt for your AI agent" sits under the quickstart and copies a start-a-product prompt; a "Preview the prompt" disclosure shows exactly the copied text in a hairline-framed mono block.
- **Feedback:** the label turns to "Copied" with a check for 2.4s (a live status says it for screen readers); a failed copy turns the control signal red with "Copy failed". Prompt text lives in `lib/agent-prompt.mjs`, whose commands are tested against the template and the New project guide.

### Brand Images
- **Favicon:** the mark in ink with the connected-green terminal; light and dark through `prefers-color-scheme` (`app/icon.svg`).
- **Home-screen icon:** the paper mark on an opaque ink tile, terminal in dark-theme green, 180px.
- **Social card:** 1200x630 chart white. Brand lockup, "The developer guide" in display 800, the lead in muted Inter, a 2px ink rule over a mono caption; on the right the module chart printed with real orders and page counts and the dashed 700 slot. Both are generated at build (`lib/brand-images.tsx`) and served from `.png` routes, so crawlers get them without a redirect.

### Navigation
- **Sidebar:** 0.8125rem entries, 34px min height, 4px radius, body-text colour; hover fills with the hover wash. Group headers are 600 ink. Nested groups hang from a 1px hairline 18px in. Modules are ordered by `ModuleOrder` so the sidebar reads like the chart. The current page alone prints as an ink tile.
- **Running head (768px and up):** one ruled bar; search, Ask AI, GitHub and theme are separated by hairline dividers, not floating pills. GitHub is pinned here, so the sidebar does not repeat it.
- **Phone bar (below 768px):** a 56px top bar, paper over the page with a light blur and a hairline: the logo (home) on the left; search, Ask AI, GitHub and the navigation drawer as 40px icon buttons on the right. It replaces Glass's floating bottom buttons; docs and home share it (`components/site-header.tsx`).
- **Home header:** the same bar at 60px, aligned to the 1360px page: lockup, the three guide sections and the configuration builder, a ground search field with its shortcut, GitHub and theme. Below 1024px search becomes an icon; below 768px the sections and theme move into a menu panel of large ruled links with a theme row.
- **Section bar (below 1280px):** "On this page" as a sticky 44px bar under the header: an ink progress ring (reading progress), the current section's title, its position as a mono `3/16`, and a chevron. It opens to the outline, depth indented, the current item in 600 ink with a 2px ink mark; Escape, an outside tap or a choice closes it. It reads the active heading from the same Fumadocs TOC state as the desktop outline (`components/section-bar.tsx`).
- **Outline (1280px and up):** "On this page" in 600 ink over a hairline track; entries 0.8125rem muted, indented by depth. The sections in view print a 2px ink thumb on the track and turn ink.
- **Back to top:** a 44px paper square with a strong hairline and a soft overlay shadow, bottom right (safe-area aware), shown once the reader is a screen and a half down; it inverts to ink on hover, moves focus to the page title, and hides while Ask AI is open. Inert while hidden.
- **Previous / next:** one ruled row under a 2px ink rule, two cells split by a hairline, a muted "Previous" or "Next" label over the page title in display 700. It replaces the stock cards.
- **Search results:** the selected option inverts to ink; matches are a cobalt tint with underline that stays legible on ink.

### Element Tile (signature)
The chart cell. A near-square family-tint cell (min 112px) with order and page count in mono at the top corners, the two-letter symbol in heavy grotesk at the bottom, and the name in ink below. Hover and focus invert it to ink with paper text (200ms). The practices series lays the same tile sideways (symbol, then name, page count in the corner, 76px tall). The page-head tile is a 64x60 version beside each docs title, linking back to the element's section; the API reference uses a slate "Ap / v1" tile.

### Your Module Cell (signature)
The dashed empty 700 slot beside Shipping. At rest: transparent, dashed strong hairline, a plus and "Your module" in muted. On hover or focus the border goes solid ink, the cell inverts, and the name is replaced by the command that fills it (`dotnet new starterkit-module`, mono).

### Request Journey (signature)
`components/request-journey.tsx`, under "Follow one request through the system". A paper plate under a 2px ink rule: twelve stations of a `POST /api/v1/products` in the order the code runs them (host pipeline, inside the module, one transaction, leaving the request), each a ground cell with a mono step number and a display label, grouped under ruled stage names. The current station prints as an ink tile (inversion only, no lift); while playing it carries a 2px timer hairline. Below, a 2px rail fills to the current station (scaleX) and a small ink diamond rides it (translateX), both 600ms. All twelve cards share one grid cell, so the panel holds the tallest card's height and nothing below it moves; the current card swaps quietly from a visible default (240ms, opacity .6 to 1, rise 4px) to the station's title, the real call in a mono chip, two sentences, a link to the guide, and a ruled "Can end the request here" list of status codes (danger tint for 4xx/5xx, connected tint for 2xx). It advances every 8s (long enough to read a station) only while on screen, pauses on hover or focus, has Pause/Play and previous/next controls with the stage and position printed beside them (no label above the title), and never auto-plays under reduced motion. Station cells tighten under 1280px and reflow to 6 columns at 1180px and 4 at 520px (stage names drop; the footer names the stage; the diamond drops because one rail cannot point at a tile in a wrapped grid, and the fill remains as progress).

### Why Showcase (signature)
`components/why-showcase.tsx`, the first stratum under the hero and the home's one full-bleed band. It always prints in the dark palette (the dark tokens are redeclared on the band, so chips, rules and focus work unchanged): in the light theme it is the dark ground, in the dark theme a deeper #06080c, painted edge to edge by a spread shadow clipped horizontally so the page never widens. Contents:
- **Pitch:** "Ship the product, not the plumbing." in display 800 at clamp(2.75-5.25rem), 0.95 leading; a muted lead; the architecture as three checked rules; a paper primary action ("Generate your product", 52px, the band's only lifted control) and an outlined secondary.
- **Terminal:** a raised paper panel (8px, rule ring and a soft overlay shadow) showing what the template really generates: the command, the ten `src/Modules` folders, the AppHost command and the four resources it starts (api, postgres, redis, seq). The commands are always printed; the output prints in once (folders 70ms apart, resources 120ms) the first time it is on screen, then a caret blinks. A screen-reader sentence states the same facts.
- **Ready on day one:** ten capability tiles in the chart's form (cells touching through a 1px rule, 6px outer radius, 5 across, 2 at 1100px, 1 at 560px): the element symbol huge in its family colour, an arrow, the title and a muted detail naming only what ships. A light follows the pointer across the grid (a radial wash of the tile's family colour, stronger on the hovered tile); the arrow nudges up-right.
- **Built on:** the real stack as outlined pills in a slow ribbon (60s loop, edge-faded mask) that pauses under the pointer and while the band is off screen; under reduced motion it is a wrapped list. It ends with a link to the technology stack page.
No numbers, logos or claims that are not in the repository.

### Route Plates
Each "way in" opens on a printed plate (a 2px cell with the cell rule) of where the path ends: the template command over four ruled cells for the generated `src/` folders, the host/modules/contracts stack (module chips in their family tints), or the slice layout as a mono tree. Hover or focus inverts the plate to ink (260ms); nothing inside it moves. Decorative for assistive tech; the link's heading and description carry the meaning.

### Guardrail Run and Colophon
The guardrails table sits in a hairline frame under a ground strip printing `$ dotnet test src/Api.Tests` over a 2px ink rule; each row carries a connected-tint tick, always printed (the tick has its own 18px grid track, so long test names wrap beside it). The footer is a colophon under a 2px ink rule: brand, one line on what the guide is, an edition row of real counts (guides, modules, decisions; mono numerals between hairlines), three ruled link columns (Build, Operate, Resources) and a footnote on where the pages come from and that the counts are taken at build time.

### Legend
Family buttons with a 10px family swatch. Pressing one dims every other tile and shrinks it slightly (scale 0.97), propagating in startup order with a 14ms stagger per tile; a live caption states the family's description. Hovering a dimmed tile restores it immediately.

### Ruled Lists and Tables
Header rule 2px ink, rows separated by 1px hairlines, no cell borders or fills, tabular figures. Used for prose tables, guardrails, decisions, the 404 "ways back" list and the three-routes row (columns split by hairlines). On mobile the guardrails table becomes stacked ruled rows.

### Configuration Builder (tool)
`/tools/config-builder/` (`components/config-builder/*`, styles in `config-builder.css`). The chart world at tool density: three panes under one 2px ink rule, split by hairlines, aligned to the 1360px header.
- **Product pane (left, sticky):** where it runs (a stacked segmented control), the product's own chart, the `dotnet new` options and the command that generates the product, then the section index with mono change and check counts (the current section prints as an ink tile).
- **The product's chart (the one bold element):** one cell per module in `ModuleOrder`, 3 across, touching through a 1px gap. An included module is its family tint with the symbol and order in the family ink; a left-out module becomes a dashed, hatched empty slot with its name struck through. Toggleable cells (samples, Search, Agentic) invert to ink on hover and focus; always-included cells carry a lock and do not invert.
- **Settings (centre):** a sticky ruled filter bar; module headings in display 800 over a 2px ink rule with their chip; each section's key in mono with Reference and Guide links. Rows are hairline-ruled: label (600), the full key in muted mono, the meaning from the reference, then the control. Label and control sit side by side once the column is 520px wide (a container query). A changed row marks its label with a 7px cobalt square and shows the default and Reset. Controls are native: switch (ink when on), segmented radios (chosen option inverts to ink), select, checkbox groups, an ordered list with up/down buttons, tag lists in mono chips, numbers with their unit. Secrets show a dashed note with the secret file, never an input.
- **Checks and export (right, sticky):** check cards use the callout recipe (7% tone fill, tinted hairline): signal red for what stops startup, ochre for recommendations, cobalt for notes; each names its keys as mono buttons that jump to the field. Exports are ink-inverting tabs over framed code blocks (ground title strip, 6px frame) highlighted with the site tokens: keys cobalt, strings green, numbers ochre, punctuation muted.
- **Below 1280px** the export moves under the sheet; **below 1024px** one pane shows at a time behind a sticky three-way ruled tab bar.
- **Docs callout:** pages that document configuration print one ruled ground line under the page tools, naming the section and linking into the builder (`lib/config-builder/doc-links.ts`, derived from the catalog).

### Motion
One easing, `cubic-bezier(.16, 1, .3, 1)`, at 160-260ms for colour and transform changes; arrows nudge 4px on hover. Sidebar disclosure uses its own collapsible curves (200ms in, 160ms out). Loading states (search index, Ask AI) share one 2px ink scanning rule.

**The home's lower half: one request, station by station.** The request journey is the authored moment below the hero (see Request Journey). Its supporting motion is tied to meaning, never a shared entrance: route plates invert on hover; the guardrail run passes each test once (tick pulse and a green row wash, 120ms apart), the first time the block comes on screen (`components/in-view.tsx`); ADR numerals invert on hover. The server render is always the finished state, so nothing is hidden without JavaScript, and text never dims while it waits.

**The one authored moment: tile to title.** Each home chart tile and the page-head tile of the same element share a React `<ViewTransition>` name (`element-<symbol>`, class `element-morph`). Opening a tile morphs it, in 380ms with a brief 2px blur, into the tile beside the new page's title; going back reverses it. The page itself swaps at once (the root crossfade is disabled), so nothing waits on the motion. Browsers without view transitions navigate normally. Do not add page-wide entrances, generic fade-up reveals or parallax.

`prefers-reduced-motion` removes every transition, animation and view-transition animation.

## Do's and Don'ts

### Do:
- **Do** fill cells with the family tint and print the symbol and numerals in the family colour; keep names in ink.
- **Do** show rank by inversion only: current page, selected search option, pressed legend button and hovered tile become ink with paper text.
- **Do** back every number on a tile with the real source (`ModuleOrder`, computed page count); drop the tile if it has no pages.
- **Do** separate structure with a 2px ink header rule and 1px hairlines instead of boxes.
- **Do** keep prose at a 70ch measure, 1rem/1.72, with H2s opening on a hairline.
- **Do** use cobalt for focus (2px outline, 3px offset), prose links and search highlights.
- **Do** reflow the chart by whole cells (6, then 3 columns; practices 4, then 2).
- **Do** keep every family token defined for both themes and reference the token, never the value.

### Don't:
- **Don't** use a family colour as a page background, heading colour or large fill.
- **Don't** add drop shadows to tiles, code blocks, tables or callouts; shadows belong only to the search action and dialogs.
- **Don't** mark the current item with a coloured side bar, underline or accent fill; it inverts to ink.
- **Don't** use mono for anything other than code and factual numerals.
- **Don't** float toolbar controls as separate pills; the running head is one ruled bar.
- **Don't** load hosted font stylesheets; fonts are bundled locally.
