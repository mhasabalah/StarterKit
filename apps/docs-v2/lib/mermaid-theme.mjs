/**
 * Concrete colour pairs are required by Mermaid's colour derivation and exported SVGs. They are the module chart's
 * flat family tints (app/global.css): paper ground, ink text and ink edges, one tint per responsibility.
 */
export const diagramPalettes = {
  light: { background: '#ffffff', surface: '#f3f4f7', text: '#0f141d', border: '#7a8396', edge: '#3d4658',
    roles: { api: ['#e3e8fa', '#2342b5'], application: ['#ebe6fa', '#5534b3'], domain: ['#dcefe6', '#0a6a49'], infrastructure: ['#f8e9d3', '#8a4a06'], data: ['#e6e8ed', '#3d4658'], external: ['#f8e3eb', '#9c2c58'], event: ['#dcefef', '#0b6870'] } },
  dark: { background: '#10141c', surface: '#161b25', text: '#eef1f6', border: '#69738a', edge: '#9ba5b7',
    roles: { api: ['#172041', '#a5b6ff'], application: ['#221a40', '#c3b2ff'], domain: ['#0f2a20', '#74d9ae'], infrastructure: ['#2e2010', '#f1b873'], data: ['#1d222c', '#c3cad6'], external: ['#331624', '#f2a3c3'], event: ['#0d2a2c', '#6fd3d9'] } },
};

/** @returns {import('mermaid').MermaidConfig} */
export function diagramTheme(dark = false) {
  const p = diagramPalettes[dark ? 'dark' : 'light'];
  const [fill, stroke] = p.roles.application;
  return {
    startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true,
    theme: 'base', look: 'classic', layout: 'dagre', fontFamily: 'Inter, ui-sans-serif, sans-serif',
    secure: ['securityLevel', 'startOnLoad', 'maxTextSize', 'suppressErrorRendering', 'secure', 'theme', 'themeVariables', 'themeCSS'],
    flowchart: { htmlLabels: false, useMaxWidth: true, curve: 'linear', nodeSpacing: 24, rankSpacing: 40, padding: 16 },
    sequence: { useMaxWidth: true, wrap: true, width: 125, actorMargin: 24, diagramMarginX: 12, messageMargin: 28 },
    state: { useMaxWidth: true, padding: 14 }, er: { useMaxWidth: true, minEntityWidth: 100 },
    themeVariables: {
      darkMode: dark, background: p.background, primaryColor: fill, primaryBorderColor: stroke, primaryTextColor: p.text,
      secondaryColor: p.roles.domain[0], secondaryBorderColor: p.roles.domain[1], secondaryTextColor: p.text,
      tertiaryColor: p.surface, tertiaryTextColor: p.text, tertiaryBorderColor: p.border,
      textColor: p.text, lineColor: p.edge, mainBkg: fill, nodeBorder: stroke, clusterBkg: p.surface, clusterBorder: p.border,
      edgeLabelBackground: p.background, fontFamily: 'Inter, ui-sans-serif, sans-serif', fontSize: '14px',
      actorBkg: fill, actorBorder: stroke, actorTextColor: p.text, actorLineColor: p.border,
      signalColor: p.edge, signalTextColor: p.text, labelBoxBkgColor: p.surface, labelBoxBorderColor: p.border,
      labelTextColor: p.text, loopTextColor: p.text, noteBkgColor: p.surface, noteBorderColor: p.border, noteTextColor: p.text,
      activationBkgColor: p.roles.event[0], activationBorderColor: p.roles.event[1], sequenceNumberColor: p.background,
    },
    themeCSS: `
      .node rect, .actor { rx: 2px; ry: 2px; }
      .node rect, .node polygon, .node path, .node circle { stroke-width: 1.25px; }
      .cluster rect { rx: 2px; ry: 2px; stroke-dasharray: 4 3; }
      .cluster-label text, .cluster-label tspan { font-weight: 600; }
      .edgeLabel rect { opacity: 1; }
      text, tspan { font-family: Inter, ui-sans-serif, sans-serif; }
      ${Object.entries(p.roles).map(([role, [bg, border]]) => `
        .node.${role} rect, .node.${role} polygon, .node.${role} circle, .node.${role} path {
          fill: ${bg} !important; stroke: ${border} !important;
        }
        .node.${role} text, .node.${role} tspan { fill: ${p.text} !important; }
      `).join('')}
      .node.port rect { stroke-dasharray: 5 3; }
    `,
  };
}
