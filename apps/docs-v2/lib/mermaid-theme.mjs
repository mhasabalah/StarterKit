/** Concrete colour pairs are required by Mermaid's colour derivation and exported SVGs. */
export const diagramPalettes = {
  light: { background: '#ffffff', surface: '#f5f3fc', text: '#1c2333', border: '#7b879e', edge: '#076e66',
    roles: { api: ['#eef3ff', '#4568ac'], application: ['#f0ebff', '#6350b8'], domain: ['#e7f5ee', '#287653'], infrastructure: ['#fff4dd', '#98670c'], data: ['#edf1f6', '#55657d'], external: ['#fbeaf3', '#a54178'], event: ['#e3f5f3', '#167d75'] } },
  dark: { background: '#10141f', surface: '#171c29', text: '#f0f2fa', border: '#68768f', edge: '#65d9ca',
    roles: { api: ['#1b2b45', '#86adf3'], application: ['#272144', '#b4a6ff'], domain: ['#12352e', '#70d6b0'], infrastructure: ['#372d18', '#e7c46a'], data: ['#222d3e', '#a3b3cc'], external: ['#392137', '#e0a0c9'], event: ['#153332', '#65d9ca'] } },
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
      .node rect, .actor { rx: 8px; ry: 8px; }
      .node rect, .node polygon, .node path, .node circle { stroke-width: 1.5px; }
      .cluster rect { rx: 10px; ry: 10px; stroke-dasharray: 4 3; }
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
