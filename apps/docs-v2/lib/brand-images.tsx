import { ImageResponse } from 'next/og';
import { modules, type Family } from '@/lib/chart';
import { ogFonts } from '@/lib/og-fonts';
import { socialSize, touchSize } from '@/lib/brand-meta';

/**
 * Generated brand images, served by static route handlers with file extensions (app/social.png, app/touch-icon.png):
 * extensionless metadata routes would answer every crawler with a trailing-slash redirect.
 */

// Light-theme chart tokens (app/global.css); a generated image cannot read CSS variables.
const ink = '#0f141d', muted = '#5a6476', ground = '#f3f4f7', hairline = '#cdd2db';
const family: Record<Family, [string, string]> = {
  foundations: ['#2342b5', '#e3e8fa'], connected: ['#0a6a49', '#dcefe6'],
  samples: ['#8a4a06', '#f8e9d3'], practices: ['#5534b3', '#ebe6fa'],
};
const pad = (order?: number) => String(order ?? '').padStart(3, '0');
const cell = { display: 'flex', flexDirection: 'column', justifyContent: 'space-between', width: 132, height: 124, padding: '12px 14px' } as const;

/** The social preview is the home page's chart, printed: real modules, real startup orders, real page counts. */
export async function socialCard() {
  const before = modules.filter((m) => (m.order ?? 0) <= 700);
  const after = modules.filter((m) => (m.order ?? 0) > 700);
  return new ImageResponse(
    <div style={{ display: 'flex', width: '100%', height: '100%', padding: 64, background: ground, color: ink, fontFamily: 'IBM Plex Sans' }}>
      <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'space-between', flex: 1, paddingRight: 40 }}>
        <div style={{ display: 'flex', alignItems: 'center' }}>
          <svg width="44" height="40" viewBox="8 16 104 96">
            <rect x="24" y="16" width="88" height="24" rx="8" fill={ink} />
            <rect x="8" y="52" width="88" height="24" rx="8" fill={ink} />
            <rect x="24" y="88" width="88" height="24" rx="8" fill={ink} />
            <rect x="88" y="88" width="24" height="24" rx="8" fill={family.connected[0]} />
          </svg>
          <div style={{ display: 'flex', marginLeft: 14, fontFamily: 'Schibsted Grotesk', fontSize: 34, letterSpacing: -0.8 }}>
            <span style={{ fontWeight: 500 }}>Starter</span><span style={{ fontWeight: 800 }}>Kit</span>
          </div>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          <div style={{ fontFamily: 'Schibsted Grotesk', fontWeight: 800, fontSize: 74, lineHeight: 0.98, letterSpacing: -2.9 }}>The developer guide</div>
          <div style={{ marginTop: 24, fontSize: 25, lineHeight: 1.4, color: muted }}>
            One ASP.NET Core 10 host, separately owned modules, boundaries enforced by tests.
          </div>
        </div>
        <div style={{ display: 'flex', paddingTop: 16, borderTop: `2px solid ${ink}`, fontFamily: 'JetBrains Mono', fontSize: 17, color: muted }}>
          Modules, in startup order
        </div>
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignContent: 'flex-start', width: 4 * 132 + 3 + 2, gap: 1, alignSelf: 'center', background: hairline, border: `1px solid ${hairline}` }}>
        {[...before.map((m) => ({ m })), { m: undefined }, ...after.map((m) => ({ m }))].map(({ m }, i) => m
          ? <div key={m.symbol} style={{ ...cell, background: family[m.family][1] }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontFamily: 'JetBrains Mono', fontSize: 15, color: family[m.family][0] }}>
              <span>{pad(m.order)}</span><span>{m.pages}</span>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              <span style={{ fontFamily: 'Schibsted Grotesk', fontWeight: 800, fontSize: 46, lineHeight: 1, letterSpacing: -1.2, color: family[m.family][0] }}>{m.symbol}</span>
              <span style={{ marginTop: 6, fontSize: 14, color: ink }}>{m.name}</span>
            </div>
          </div>
          : <div key={`yours-${i}`} style={{ ...cell, background: ground, border: `2px dashed #9aa3b2` }}>
            <span style={{ fontFamily: 'JetBrains Mono', fontSize: 15, color: muted }}>700</span>
            <span style={{ fontSize: 14, color: muted }}>Your module</span>
          </div>)}
        <div style={{ ...cell, background: ground }} />
      </div>
    </div>,
    { ...socialSize, fonts: await ogFonts() },
  );
}

/** Home-screen icon: the mark in paper on an opaque ink tile, with the terminal slice in connected green. */
export function touchIcon() {
  return new ImageResponse(
    <div style={{ display: 'flex', width: '100%', height: '100%', alignItems: 'center', justifyContent: 'center', background: '#0f141d' }}>
      <svg width="112" height="104" viewBox="8 16 104 96">
        <rect x="24" y="16" width="88" height="24" rx="8" fill="#ffffff" />
        <rect x="8" y="52" width="88" height="24" rx="8" fill="#ffffff" />
        <rect x="24" y="88" width="88" height="24" rx="8" fill="#ffffff" />
        <rect x="88" y="88" width="24" height="24" rx="8" fill="#74d9ae" />
      </svg>
    </div>,
    touchSize,
  );
}
