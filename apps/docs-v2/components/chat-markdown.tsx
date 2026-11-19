'use client';
import Link from 'next/link';
import type { ReactNode } from 'react';
import { unified } from 'unified';
import remarkParse from 'remark-parse';
import remarkGfm from 'remark-gfm';
import type { Nodes } from 'mdast';

export type ChatSource = { title: string; url: string };

/**
 * Renders a model's answer as Markdown without ever producing raw HTML. The text is parsed to a syntax tree and each
 * node maps to an allowlisted React element; React escapes every string. HTML in the answer prints as text, images
 * become links (nothing is fetched), and only relative, fragment, http and https links are clickable.
 */
const parser = unified().use(remarkParse).use(remarkGfm);

export function safeHref(url: string): string | undefined {
  if (/^\/(?!\/)/.test(url) || url.startsWith('#')) return url; // site-relative or fragment; never protocol-relative
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.href : undefined;
  } catch { return undefined; }
}

function SafeLink({ href, children }: { href: string | undefined; children: ReactNode }) {
  if (!href) return <>{children}</>;
  if (href.startsWith('/')) return <Link href={href}>{children}</Link>;
  if (href.startsWith('#')) return <a href={href}>{children}</a>;
  return <a href={href} target="_blank" rel="noopener noreferrer">{children}</a>;
}

/** `[1]` cites the first source the server returned; a citation without a matching source stays text. */
function cite(text: string, sources: ChatSource[]): ReactNode {
  const parts = text.split(/(\[\d{1,2}\])/);
  if (parts.length === 1) return text;
  return parts.map((part, i) => {
    const source = /^\[(\d{1,2})\]$/.exec(part) && sources[Number(part.slice(1, -1)) - 1];
    if (!source) return part;
    const n = part.slice(1, -1);
    return <Link key={i} className="chat-cite" href={source.url} title={source.title} aria-label={`Source ${n}: ${source.title}`}>{n}</Link>;
  });
}

function render(node: Nodes, sources: ChatSource[], key?: number): ReactNode {
  const children = () => ('children' in node ? node.children.map((child, i) => render(child as Nodes, sources, i)) : null);
  switch (node.type) {
    case 'root': return <>{children()}</>;
    case 'paragraph': return <p key={key}>{children()}</p>;
    // Answers sit in a narrow panel under the page's own headings, so their headings print as strong lines.
    case 'heading': return <p key={key} className="chat-md-heading"><strong>{children()}</strong></p>;
    case 'text': return <span key={key}>{cite(node.value, sources)}</span>;
    case 'emphasis': return <em key={key}>{children()}</em>;
    case 'strong': return <strong key={key}>{children()}</strong>;
    case 'delete': return <del key={key}>{children()}</del>;
    case 'inlineCode': return <code key={key}>{node.value}</code>;
    case 'code': return <pre key={key}><code data-language={node.lang ?? undefined}>{node.value}</code></pre>;
    case 'break': return <br key={key} />;
    case 'thematicBreak': return <hr key={key} />;
    case 'blockquote': return <blockquote key={key}>{children()}</blockquote>;
    case 'list': return node.ordered
      ? <ol key={key} start={node.start && node.start !== 1 ? node.start : undefined}>{children()}</ol>
      : <ul key={key}>{children()}</ul>;
    case 'listItem': return <li key={key}>{children()}</li>;
    case 'link': return <SafeLink key={key} href={safeHref(node.url)}>{children()}</SafeLink>;
    case 'image': return <SafeLink key={key} href={safeHref(node.url)}>{node.alt || node.url}</SafeLink>;
    case 'table': return <div key={key} className="chat-md-table"><table>
      <thead><tr>{node.children[0]?.children.map((cell, i) => <th key={i} scope="col">{cell.children.map((child, j) => render(child, sources, j))}</th>)}</tr></thead>
      <tbody>{node.children.slice(1).map((row, i) => render(row, sources, i))}</tbody>
    </table></div>;
    case 'tableRow': return <tr key={key}>{children()}</tr>;
    case 'tableCell': return <td key={key}>{children()}</td>;
    case 'html': return <span key={key}>{node.value}</span>; // printed, never parsed
    case 'definition': case 'footnoteDefinition': case 'yaml': return null;
    default: return 'children' in node ? <span key={key}>{children()}</span> : 'value' in node ? <span key={key}>{String(node.value)}</span> : null;
  }
}

export default function ChatMarkdown({ text, sources }: { text: string; sources: ChatSource[] }) {
  return <div className="chat-md">{render(parser.parse(text), sources)}</div>;
}
