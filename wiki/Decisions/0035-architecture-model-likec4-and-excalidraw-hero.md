# 0035. Describe the running system with one LikeC4 architecture model, plus one hand-drawn big picture

System design views (landscape, containers, module drill-downs, request flows, deployment) are projections of one
architecture model written in LikeC4 under `wiki/Architecture/model/`. One hand-drawn Excalidraw figure gives the big
picture at the top of [System Design](../Architecture/System-Design.md). Mermaid stays for detailed sequences, states
and data models.

## Status

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

The documentation explained each module in depth but had no view of the running system: one six-node context figure,
and no diagram showing which stores, queues, workers and providers a module touches, or what happens after a user
acts. Readers needed to go from the big picture to a module, and from a module to a flow, without the pictures
contradicting each other.

Constraints:
<!--#if (templateSource)-->
- `wiki/` is plain GitHub Markdown, with no raw HTML and no site-specific syntax beyond fenced code blocks
  ([ADR-0024](0024-documentation-site.md) for the template's documentation sites).
<!--#else-->
- `wiki/` is plain GitHub Markdown, with no raw HTML and no site-specific syntax beyond fenced code blocks.
<!--#endif-->
- Every diagram names real types, has an accessible title and description, and reads in light and dark themes
  ([diagram style](../Reference/Diagram-Style.md)).
<!--#if (templateSource)-->
- Optional modules (Catalog, Shipping, Agentic) are removed from generated products by excluding files; nothing may
  depend on conditional markers inside diagram sources ([ADR-0020](0020-dotnet-new-templates.md)).
<!--#endif-->

## Decision

1. **One model, many views.** The system is modelled once in LikeC4 (`wiki/Architecture/model/*.c4`): actors,
   Nginx, the application container, the migrator, PostgreSQL, Redis, the message transport, blob storage, ClamAV,
   observability, every module with its endpoints, components, adapters, jobs and consumers, and the external
   providers. Element views, dynamic (flow) views and a deployment view are declared in the same files.
2. **One file per module, owning every edge that mentions it.** `system.c4` holds the shared elements, and each module
   file extends `starterkit.app`. A relationship that involves an optional module lives in that module's file, so
   excluding the file removes the module from every view. Cross-module views use wildcards.
3. **Pages embed views by id.** A fenced block with the language `likec4-view` whose body is a view id places that
   view in a page, followed by an italic `*Figure: …*` caption. On GitHub the fence shows the id; the caption and the
   page's runtime-components table carry the meaning.
4. **Every view has a `title` and a one-sentence `description`**, the equivalents of Mermaid's `accTitle` and
   `accDescr`. Colors come from the documentation roles defined once in `specification.c4`; views carry no colors
   of their own.
5. **Views render interactively.** Outside a documentation site, `npx likec4 start wiki/Architecture/model` serves
   every view. The template's documentation site renders them in place: the model is validated (`likec4 validate`) and compiled
   with `likec4 gen react` into a module that is loaded lazily, in the browser only, on pages that embed a view. The
   inline view is a fitted overview; clicking it opens LikeC4's browser to zoom, search, open element details, drill
   into nested views and step through flows. The build fails on an invalid model or an unknown view id. Fonts come
   from the site, never from a CDN.
6. **One hand-drawn hero.** `wiki/Architecture/diagrams/system-big-picture.excalidraw.svg` is an SVG with the
   Excalidraw scene embedded, so it opens for editing in Excalidraw or its VS Code extension. It names only the
   modules every product has and is inverted for the dark theme by the site, as Excalidraw itself does.
7. **Mermaid keeps its job.** Sequences inside one module, state machines, entity relationships and class diagrams
   stay in Mermaid next to the prose they explain.

## Consequences

- The big picture, the module views and the flows cannot drift apart: they are the same elements.
- Changing a module's stores, jobs, events or providers means changing its `.c4` file in the same pull request.
- The model can be browsed without the site: `npx likec4 start wiki/Architecture/model`.
- Accessibility: the inline view mirrors its caption, so it is kept out of the tab order and the accessibility
  tree; the caption, the page's runtime-components table and the **Explore** button carry it. `likec4.css` keeps
  relationship labels opaque and at full strength so their text meets 4.5:1 in both themes.
- LikeC4 views are wider than the 860px budget of static Mermaid figures. The inline view is an overview; detail is
  read in the full-screen browser, and the facts are repeated as text in each module's runtime-components table.
- The site carries a larger client bundle on the pages that embed views, loaded on demand.
- Enforcement: `likec4 validate` rejects an invalid model.
<!--#if (templateSource)-->
- In the template's documentation site, the build also rejects an unknown view id; a unit test checks titles,
  descriptions, orphan views, role contrast and the review record of each model file; browser tests check rendering
  in both themes. LikeC4's React bundle targets Vite and reads `import.meta.env`; the site defines those values in its
  webpack configuration.
<!--#endif-->

## Alternatives considered

- **Excalidraw only.** Pleasant to read, but each diagram redraws the same boxes by hand, diffs are unreadable, and
  nothing ties a box to the code. Kept for the single hero, where the hand-drawn style earns its cost.
- **D2.** Good layouts and a sketch style, but diagrams share no model, so the same element is repeated per file,
  and CI needs the d2 binary.
- **Structurizr DSL.** The same C4 idea, but rendering needs a Java server or a hosted service, and embedding
  interactive views in the documentation site is harder.
- **More Mermaid.** No shared model and no drill-down; `C4Context` support is still limited in Mermaid.

## Related

[System Design](../Architecture/System-Design.md), [Diagram style](../Reference/Diagram-Style.md),
[ADR index](README.md) and `/starterkit:update-docs`.
