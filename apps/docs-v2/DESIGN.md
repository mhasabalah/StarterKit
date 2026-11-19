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

Strata below the hero (three ways in; guardrails and decisions; footer) are separated by a 1px rule and a deep vertical gap (clamp 56-96px). Their content hangs from a 2px ink rule.

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

### Cards / Containers
- **Corner Style:** 6px for code and diagrams.
- **Background:** paper body, ground title strip.
- **Shadow Strategy:** none (see Elevation).
- **Border:** 1px hairline; title strips are separated by a hairline.
- **Internal Padding:** diagram canvas clamp(12-24px).

### Inputs / Fields
- **Style:** the chat textarea is paper with a 1px hairline and 6px radius; the search field in the running head is ground-filled with a hairline, min 240px wide.
- **Focus:** 2px cobalt outline; inside the search dialog the header shows a 2px cobalt underline instead. While the index loads, a 2px ink scanning rule slides along the dialog header.
- **Disabled:** 50% opacity, default cursor.

### Navigation
- **Sidebar:** 0.8125rem entries, 34px min height, 4px radius, body-text colour; hover fills with the hover wash. Group headers are 600 ink. Nested groups hang from a 1px hairline 18px in. Modules are ordered by `ModuleOrder` so the sidebar reads like the chart. The current page alone prints as an ink tile.
- **Running head:** one ruled bar; controls are separated by hairline dividers, not floating pills.
- **TOC:** 0.8125rem muted links, active and hover in ink with an ink marker.
- **Search results:** the selected option inverts to ink; matches are a cobalt tint with underline that stays legible on ink.

### Element Tile (signature)
The chart cell. A near-square family-tint cell (min 112px) with order and page count in mono at the top corners, the two-letter symbol in heavy grotesk at the bottom, and the name in ink below. Hover and focus invert it to ink with paper text (200ms). The practices series lays the same tile sideways (symbol, then name, page count in the corner, 76px tall). The page-head tile is a 64x60 version beside each docs title, linking back to the element's section; the API reference uses a slate "Ap / v1" tile.

### Your Module Cell (signature)
The dashed empty 700 slot beside Shipping. At rest: transparent, dashed strong hairline, a plus and "Your module" in muted. On hover or focus the border goes solid ink, the cell inverts, and the name is replaced by the command that fills it (`dotnet new starterkit-module`, mono).

### Legend
Family buttons with a 10px family swatch. Pressing one dims every other tile and shrinks it slightly (scale 0.97), propagating in startup order with a 14ms stagger per tile; a live caption states the family's description. Hovering a dimmed tile restores it immediately.

### Ruled Lists and Tables
Header rule 2px ink, rows separated by 1px hairlines, no cell borders or fills, tabular figures. Used for prose tables, guardrails, decisions, the 404 "ways back" list and the three-routes row (columns split by hairlines). On mobile the guardrails table becomes stacked ruled rows.

### Motion
One easing, `cubic-bezier(.16, 1, .3, 1)`, at 160-260ms for colour and transform changes; arrows nudge 4px on hover. Sidebar disclosure uses its own collapsible curves (200ms in, 160ms out). `prefers-reduced-motion` removes every transition and animation.

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
