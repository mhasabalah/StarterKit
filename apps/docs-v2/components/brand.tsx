/** The StarterKit mark (three offset slices, distinct terminal) drawn in the chart palette, with a live wordmark. */
export function BrandMark({ className = 'brand-mark' }: { className?: string }) {
  return <svg className={className} viewBox="8 16 104 96" aria-hidden="true" focusable="false">
    <rect x="24" y="16" width="88" height="24" rx="8" />
    <rect x="8" y="52" width="88" height="24" rx="8" />
    <rect x="24" y="88" width="88" height="24" rx="8" />
    <rect className="brand-mark-terminal" x="88" y="88" width="24" height="24" rx="8" />
  </svg>;
}

export function BrandLogo() {
  return <span className="brand-logo" role="img" aria-label="StarterKit">
    <BrandMark />
    <span className="brand-wordmark" aria-hidden="true">Starter<b>Kit</b></span>
  </span>;
}
