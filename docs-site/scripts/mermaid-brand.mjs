import { readFileSync } from 'node:fs';

// Base variables are concrete colors because Mermaid derives additional colors
// with a color parser. Live SVG CSS below follows the site's CSS tokens instead.
const css = readFileSync(new URL('../src/styles/brand.css', import.meta.url), 'utf8');
const light = css.match(/:root\[data-theme='light'\]\s*\{([^}]+)\}/)[1];
const token = (name) => light.match(new RegExp(`--brand-${name}:\\s*([^;]+)`))[1];
export const mermaidOptions = {
  theme: 'base', autoTheme: false, enableLog: false,
  mermaidConfig: {
    securityLevel: 'strict',
    fontFamily: 'Inter, sans-serif',
    flowchart: { padding: 20, curve: 'basis', htmlLabels: false, useMaxWidth: true },
    sequence: { useMaxWidth: true, wrap: true, width: 120, actorMargin: 24, wrapPadding: 12 },
    state: { useMaxWidth: true },
    er: { useMaxWidth: true },
    themeVariables: {
      primaryColor: token('primary-low'), primaryBorderColor: token('primary'),
      primaryTextColor: token('text'), lineColor: token('teal'),
      secondaryColor: token('tip-bg'), tertiaryColor: token('surface'),
      background: token('bg'), mainBkg: token('primary-low'),
      textColor: token('text'), fontFamily: 'Inter, sans-serif',
    },
    themeCSS: `
      text, tspan, .nodeLabel, .edgeLabel, .label, .messageText, .loopText,
      .noteText, .labelText, .cluster-label, .entityLabel {
        fill: var(--brand-text) !important; color: var(--brand-text) !important;
        font-family: Inter, sans-serif !important;
      }
      .node rect, .node polygon, .node circle, .node path, rect.actor,
      .labelBox, .entityBox, .er.entityBox, .stateGroup rect {
        fill: var(--brand-primary-low) !important; stroke: var(--brand-primary) !important;
      }
      .node rect, .actor, .stateGroup rect { rx: 8px; ry: 8px; }
      .cluster rect, .note, .edgeLabel rect, .labelBkg,
      .attributeBoxOdd, .attributeBoxEven {
        fill: var(--brand-surface) !important; stroke: var(--brand-border) !important;
      }
      .edgePath path, .flowchart-link, .transition, .relationshipLine,
      .messageLine0, .messageLine1, .actor-line, .loopLine {
        stroke: var(--brand-teal) !important;
      }
      marker path, marker polygon, marker circle, .arrowheadPath {
        fill: var(--brand-teal) !important; stroke: var(--brand-teal) !important;
      }
      .sequenceNumber { fill: var(--brand-bg) !important; }
      .edgeLabel { background-color: var(--brand-surface) !important; }
    `,
  },
};
