'use client';
import { useMemo, useState } from 'react';
import { Check, Copy, Download } from 'lucide-react';
import { tokenize } from '@/lib/config-builder/format.mjs';

type Props = { title: string; code: string; language: 'json' | 'yaml' | 'shell'; fileName?: string; empty?: string };

/** A framed code block (ground title strip, hairline frame) with Copy and Download, highlighted with the site tokens. */
export function CodeView({ title, code, language, fileName, empty }: Props) {
  const tokens = useMemo(() => tokenize(code, language) as [string, string][], [code, language]);
  if (!code.trim()) return <div className="cb-code cb-code-empty"><div className="cb-code-head"><span>{title}</span></div><p>{empty ?? 'Nothing to export yet.'}</p></div>;
  return <figure className="cb-code">
    <figcaption className="cb-code-head">
      <span className="cb-code-title">{title}</span>
      <span className="cb-code-actions">
        <CopyButton text={code} label={`Copy ${title}`} />
        {fileName && <button type="button" className="cb-tool" onClick={() => download(fileName, code)} aria-label={`Download ${fileName}`}><Download aria-hidden="true" />Download</button>}
      </span>
    </figcaption>
    <pre tabIndex={0} aria-label={title}><code>{tokens.map(([kind, text], index) => (kind === 'text' ? text : <span key={index} className={`cb-t-${kind}`}>{text}</span>))}</code></pre>
  </figure>;
}

export function CopyButton({ text, label, children }: { text: string | (() => string); label: string; children?: React.ReactNode }) {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(typeof text === 'function' ? text() : text);
      setStatus('copied');
    } catch {
      setStatus('failed');
    }
    window.setTimeout(() => setStatus('idle'), 2400);
  };
  return <button type="button" className="cb-tool" data-status={status} onClick={copy} aria-label={label}>
    {status === 'copied' ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}
    {status === 'copied' ? 'Copied' : status === 'failed' ? 'Copy failed' : children ?? 'Copy'}
    <span className="sr-only" role="status">{status === 'copied' ? 'Copied to the clipboard' : status === 'failed' ? 'Copy failed' : ''}</span>
  </button>;
}

export function download(fileName: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
