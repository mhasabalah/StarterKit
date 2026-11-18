'use client';
import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { publicPath } from '@/lib/site';

type Message = { role: 'user' | 'assistant'; content: string; sources?: { title: string; url: string }[] };

export function AIChatPanel({ onClose }: { onClose: () => void }) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [question, setQuestion] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const controller = useRef<AbortController | null>(null);
  const end = useRef<HTMLDivElement>(null);
  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => { end.current?.scrollIntoView({ block: 'nearest' }); }, [messages, busy, error]);

  async function send() {
    if (busy || !question.trim()) return;
    const next: Message[] = [...messages, { role: 'user', content: question.trim() }];
    setMessages(next); setQuestion(''); setBusy(true); setError('');
    controller.current = new AbortController();
    try {
      const response = await fetch(publicPath('/api/chat/'), { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages: next.slice(-10).map(({ role, content }) => ({ role, content })) }), signal: controller.current.signal });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || 'Unable to answer. Please try again.');
      setMessages([...next, { role: 'assistant', content: result.answer, sources: result.sources }]);
    } catch (cause) {
      setError(cause instanceof Error && cause.name === 'AbortError' ? 'Answer stopped.' : cause instanceof Error ? cause.message : 'Unable to answer.');
    } finally { setBusy(false); }
  }
  return <section className="docs-chat" aria-label="Ask AI">
    <header><div><strong>Ask AI</strong><p>Answers from the StarterKit docs</p></div><button type="button" onClick={onClose} aria-label="Close AI chat">✕</button></header>
    <div className="docs-chat-messages" role="log" aria-live="polite" aria-busy={busy}>
      {!messages.length && <div><h2>How can I help?</h2><p>Ask about architecture, modules, deployment, or the API.</p>
        {['How do modules communicate?', 'How does tenant isolation work?', 'How do I create a new project?'].map((text) => <button className="docs-chat-suggestion" key={text} onClick={() => setQuestion(text)}>{text}</button>)}
      </div>}
      {messages.map((message, i) => <article key={i} className="docs-chat-message"><strong>{message.role === 'user' ? 'You' : 'Assistant'}</strong><p>{message.content}</p>
        {message.sources?.length ? <nav aria-label="Answer sources"><span>Sources</span>{message.sources.map((source) => <Link key={source.url} href={source.url}>{source.title}</Link>)}</nav> : null}</article>)}
      {busy && <p role="status">Reading the documentation…</p>}
      {error && <p role="alert">{error}</p>}<div ref={end} />
    </div>
    <form onSubmit={(event) => { event.preventDefault(); void send(); }}>
      <label className="sr-only" htmlFor="docs-ai-question">Your question</label>
      <textarea id="docs-ai-question" value={question} onChange={(event) => setQuestion(event.target.value)} placeholder="Ask a question…" maxLength={2000} disabled={busy} />
      <div><small>AI can make mistakes. Check the sources.</small>{busy ? <button type="button" onClick={() => controller.current?.abort()}>Stop</button> : <button disabled={!question.trim()} type="submit">Send</button>}</div>
      <button type="button" disabled={busy || !messages.length} onClick={() => { setMessages([]); setError(''); }}>New conversation</button>
    </form>
  </section>;
}
