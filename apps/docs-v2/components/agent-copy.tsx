'use client';
import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { Bot, Check, ChevronRight } from 'lucide-react';
import { pagePrompt, startPrompt } from '@/lib/agent-prompt.mjs';
import { basePath } from '@/lib/site';

type State = 'idle' | 'copied' | 'failed';
const noop = () => () => {};
/** The documentation root as this reader sees it, so prompt links work on every deployment and preview. */
function useDocsRoot() {
  return useSyncExternalStore(noop, () => `${window.location.origin}${basePath}`, () => basePath);
}

/**
 * Copies text that may still be loading. Passing a promise to ClipboardItem keeps the click's user activation in
 * Safari while the page's Markdown is fetched; browsers without it fall back to writeText.
 */
async function copy(text: string | Promise<string>) {
  if (typeof ClipboardItem !== 'undefined' && navigator.clipboard?.write) {
    try {
      const blob = Promise.resolve(text).then((value) => new Blob([value], { type: 'text/plain' }));
      await navigator.clipboard.write([new ClipboardItem({ 'text/plain': blob })]);
      return;
    } catch { /* fall through */ }
  }
  await navigator.clipboard.writeText(await text);
}

function useCopy() {
  const [state, setState] = useState<State>('idle');
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  async function run(text: string | Promise<string>) {
    clearTimeout(timer.current);
    try { await copy(text); setState('copied'); } catch { setState('failed'); }
    timer.current = setTimeout(() => setState('idle'), 2400);
  }
  return [state, run] as const;
}

function CopyButton({ state, label, onCopy }: { state: State; label: string; onCopy: () => void }) {
  return <>
    <button type="button" className="agent-copy" data-state={state} onClick={onCopy}>
      {state === 'copied' ? <Check aria-hidden="true" /> : <Bot aria-hidden="true" />}
      {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : label}
    </button>
    <span className="sr-only" role="status">{state === 'copied' ? 'Prompt copied. Paste it into your AI agent.' : state === 'failed' ? 'The prompt could not be copied.' : ''}</span>
  </>;
}

/** Home: a prompt that walks an agent through starting a product, with a preview of exactly what is copied. */
export function CopyStartPrompt() {
  const docs = useDocsRoot();
  const [state, run] = useCopy();
  const prompt = startPrompt(docs);
  return <div className="agent-start">
    <CopyButton state={state} label="Copy prompt for your AI agent" onCopy={() => run(startPrompt(`${window.location.origin}${basePath}`))} />
    <details className="agent-preview">
      <summary><ChevronRight aria-hidden="true" />Preview the prompt</summary>
      <pre tabIndex={0}>{prompt}</pre>
    </details>
  </div>;
}

/** Docs pages: the page as Markdown wrapped with the product context, the rules and a slot for the reader's task. */
export function CopyForAgent({ title, path, markdownUrl }: { title: string; path: string; markdownUrl: string }) {
  const [state, run] = useCopy();
  return <CopyButton state={state} label="Copy for agent" onCopy={() => run(
    fetch(markdownUrl).then((response) => {
      if (!response.ok) throw new Error(`Markdown request failed: ${response.status}`);
      return response.text();
    }).then((markdown) => pagePrompt({ docs: `${window.location.origin}${basePath}`, title, path, markdown })),
  )} />;
}
