'use client';
import Link from 'next/link';
import { Fragment, useId, useState, type FormEvent } from 'react';
import { BookOpen, ChevronDown, ChevronsDownUp, ChevronsUpDown, Plus, RotateCcw, Search, Trash2 } from 'lucide-react';
import { modules, dictionaries, decisions, decisionLabels } from '@/lib/config-builder/overlay.mjs';
import { humanize } from '@/lib/config-builder/model.mjs';
import { fieldId, sectionId, formatBytes } from '@/lib/config-builder/format.mjs';
import { FieldControl, isGroupControl } from './field-control';
import type { Catalog, Field, Issue } from './types';

export type Row = { field: Field; value: unknown; changed: boolean };
export type SectionView = { section: string; moduleId: string; rows: Row[]; hidden: number; changes: number; total: number };

type Props = {
  catalog: Catalog;
  sections: SectionView[];
  decisionRows: Row[];
  query: string;
  onQuery: (query: string) => void;
  showAll: boolean;
  onShowAll: (value: boolean) => void;
  changedOnly: boolean;
  onChangedOnly: (value: boolean) => void;
  totalVisible: number;
  issuesByPath: Map<string, Issue[]>;
  isOpen: (section: string) => boolean;
  onToggle: (section: string, open: boolean) => void;
  onToggleAll: (open: boolean) => void;
  onOpenSection: (section: string) => void;
  secretNote: (field: Field) => string;
  valueAt: (path: string) => unknown;
  onChange: (field: Field, value: unknown) => void;
  onReset: (field: Field) => void;
  onAddEntry: (dictionary: string, name: string) => void;
  onRemoveEntry: (dictionary: string, name: string) => void;
  entriesOf: (dictionary: string) => string[];
};

const display = (value: unknown) => {
  if (value === null || value === undefined || value === '') return 'empty';
  if (Array.isArray(value)) return value.length ? value.join(', ') : 'empty list';
  return String(value);
};
const plural = (count: number, one: string, many = `${one}s`) => `${count} ${count === 1 ? one : many}`;

/** Key decisions first, then every section in module startup order, folded to a summary until opened. */
export function SettingsPanel(props: Props) {
  const { sections, query, onQuery, showAll, onShowAll, changedOnly, onChangedOnly, totalVisible, onToggleAll, isOpen } = props;
  const filterId = useId();
  const narrowing = Boolean(query.trim()) || changedOnly;
  const shown = sections.reduce((sum, section) => sum + section.rows.length, 0);
  const allOpen = sections.length > 0 && sections.every((section) => isOpen(section.section));
  return <div className="cb-settings">
    <div className="cb-toolbar" role="search">
      <label htmlFor={filterId} className="sr-only">Filter settings</label>
      <div className="cb-filter"><Search aria-hidden="true" />
        <input id={filterId} type="search" value={query} placeholder={`Find a setting among ${totalVisible}`} title="Matches the key, the name and the meaning" onChange={(event) => onQuery(event.target.value)} autoComplete="off" spellCheck={false} />
      </div>
      <div className="cb-toggles">
        <label className="cb-check"><input type="checkbox" checked={changedOnly} onChange={(event) => onChangedOnly(event.target.checked)} /><span>Changed only</span></label>
        <label className="cb-check" title="Off: the settings most products change. On: every key in the files."><input type="checkbox" checked={showAll} onChange={(event) => onShowAll(event.target.checked)} /><span>Every key</span></label>
      </div>
      <p className="cb-toolbar-count" role="status" aria-live="polite">
        {narrowing ? `${plural(shown, 'match', 'matches')} in ${plural(sections.length, 'section')}` : `${plural(sections.length, 'section')}${showAll ? ', every key' : ''}`}
      </p>
      {!narrowing && sections.length > 0 && <button type="button" className="cb-tool cb-tool-quiet cb-toolbar-fold" onClick={() => onToggleAll(!allOpen)}>
        {allOpen ? <ChevronsDownUp aria-hidden="true" /> : <ChevronsUpDown aria-hidden="true" />}{allOpen ? 'Collapse all' : 'Expand all'}
      </button>}
    </div>

    {!narrowing && <Decisions {...props} />}

    {sections.length === 0 && <div className="cb-empty">
      <p>{changedOnly && !query.trim() ? 'Nothing is changed yet: every value is the shipped default. Start with the key decisions.' : `No setting matches “${query.trim()}”. Try part of a key, such as Smtp or Retention.`}</p>
      <button type="button" className="cb-tool" onClick={() => { onQuery(''); onChangedOnly(false); }}>Show every section</button>
    </div>}
    {modules.map((module) => {
      const own = sections.filter((section) => section.moduleId === module.id);
      if (!own.length) return null;
      return <div key={module.id} className="cb-module" data-family={module.family}>
        <h2 className="cb-module-title"><span className="cb-chip" aria-hidden="true">{module.symbol}</span>{module.name}
          <span className="cb-module-lead">{module.description}</span></h2>
        {own.map((section) => <SectionBlock key={section.section} {...props} view={section} />)}
      </div>;
    })}
  </div>;
}

/** The handful of choices that shape a deployment, grouped by the question they answer. */
function Decisions(props: Props) {
  const { decisionRows, onOpenSection } = props;
  const byPath = new Map(decisionRows.map((row) => [row.field.path, row]));
  const groups = decisions.map((group) => ({ ...group, rows: group.keys.map((key) => byPath.get(key)).filter((row): row is Row => Boolean(row)) })).filter((group) => group.rows.length);
  if (!groups.length) return null;
  return <section className="cb-decisions" aria-labelledby="cb-decisions-title">
    <h2 id="cb-decisions-title" className="cb-decisions-title">Key decisions</h2>
    <p className="cb-decisions-lead">The choices that shape a deployment. Everything below has a working default; open a section only when you need to change it.</p>
    <div className="cb-decision-groups">
      {groups.map((group) => <div key={group.id} className="cb-decision-group">
        <h3>{group.title}</h3>
        {group.rows.map((row) => <FieldRow key={row.field.path} {...props} row={row} compact label={decisionLabels[row.field.path as keyof typeof decisionLabels]}
          more={<button type="button" className="cb-more" onClick={() => onOpenSection(row.field.section)} aria-label={`Open the ${row.field.section} section`}>{row.field.section} section</button>} />)}
      </div>)}
    </div>
  </section>;
}

function groupLabel(group: string, section: string, valueAt: (path: string) => unknown) {
  if (group === section) return '';
  const segments = group.split(':');
  return segments.slice(1).map((segment, index) => {
    if (!/^\d+$/.test(segment)) return humanize(segment);
    const name = valueAt(`${segments.slice(0, index + 2).join(':')}:Name`);
    return typeof name === 'string' && name ? `“${name}”` : `#${Number(segment) + 1}`;
  }).join(' / ');
}

function SectionBlock(props: Props & { view: SectionView }) {
  const { catalog, view, valueAt, onRemoveEntry, issuesByPath, isOpen, onToggle } = props;
  const meta = catalog.sections[view.section];
  const open = isOpen(view.section);
  const id = sectionId(view.section);
  const checks = [...issuesByPath.keys()].filter((path) => path.split(':')[0] === view.section).length;
  const groups: { group: string; rows: Row[] }[] = [];
  for (const row of view.rows) {
    const last = groups.at(-1);
    if (last && last.group === row.field.group) last.rows.push(row);
    else groups.push({ group: row.field.group, rows: [row] });
  }
  // Add forms go after the last group of each dictionary in this section (or at the end when it has no entries).
  const sectionDictionaries = Object.keys(dictionaries).filter((path) => path.split(':')[0] === view.section);
  const lastGroupOf = new Map(sectionDictionaries.map((path) => [path, groups.findLastIndex((group) => group.group.startsWith(`${path}:`))]));
  return <section id={id} className="cb-section" data-open={open || undefined} aria-labelledby={`${id}-title`} data-section={view.section}>
    <header className="cb-section-head">
      <h3 id={`${id}-title`}>
        <button type="button" id={`${id}-toggle`} className="cb-section-toggle" aria-expanded={open} aria-controls={`${id}-body`} onClick={() => onToggle(view.section, !open)}>
          <ChevronDown aria-hidden="true" className="cb-section-chevron" />
          <code>{view.section}</code>
          <span className="cb-section-summary">
            <span>{plural(view.total, 'setting')}</span>
            {view.changes > 0 && <span className="cb-count">{view.changes} changed</span>}
            {checks > 0 && <span className="cb-count cb-count-check">{plural(checks, 'check')}</span>}
          </span>
        </button>
      </h3>
      <span className="cb-section-links">
        {meta?.anchor && <Link href={meta.anchor} className="cb-doc-link"><BookOpen aria-hidden="true" />Reference</Link>}
        {meta?.guide && meta.guide !== meta.anchor && <Link href={meta.guide} className="cb-doc-link">Guide</Link>}
      </span>
    </header>
    {open && <div id={`${id}-body`} className="cb-section-body">
      {groups.map((group, index) => {
        const label = groupLabel(group.group, view.section, valueAt);
        const entry = group.rows[0].field.dictionary;
        const isEntryHead = entry && group.group === `${entry.path}:${entry.entry}` && (index === 0 || groups[index - 1].group !== group.group);
        return <Fragment key={`${group.group}-${index}`}>
          <div className="cb-group" data-entry={entry ? true : undefined}>
            {label && <div className="cb-group-head"><h4>{label}</h4>
              {isEntryHead && entry.removable && <button type="button" className="cb-tool cb-tool-quiet" onClick={() => onRemoveEntry(entry.path, entry.entry)}><Trash2 aria-hidden="true" />Remove {dictionaries[entry.path as keyof typeof dictionaries].label} {entry.entry}</button>}
            </div>}
            {group.rows.map((row) => <FieldRow key={row.field.path} {...props} row={row} />)}
          </div>
          {sectionDictionaries.filter((path) => lastGroupOf.get(path) === index).map((path) => <AddEntry key={path} {...props} dictionary={path} />)}
        </Fragment>;
      })}
      {sectionDictionaries.filter((path) => lastGroupOf.get(path) === -1 && !props.query.trim() && !props.changedOnly).map((path) => <AddEntry key={path} {...props} dictionary={path} />)}
      {view.hidden > 0 && <p className="cb-section-more">{plural(view.hidden, 'more key')} with working defaults. <button type="button" className="cb-more" onClick={() => props.onShowAll(true)}>Show every key</button></p>}
    </div>}
  </section>;
}

function FieldRow({ row, issuesByPath, secretNote, onChange, onReset, compact, more, label }: Props & { row: Row; compact?: boolean; more?: React.ReactNode; label?: string }) {
  const { field, value, changed } = row;
  const id = compact ? `cb-d-${field.path.replace(/[^\w-]/g, '-')}` : fieldId(field.path);
  const describedBy = `${id}-meaning`;
  const issues = issuesByPath.get(field.path) ?? [];
  const severity = issues.some((issue) => issue.severity === 'blocking') ? 'blocking' : issues.some((issue) => issue.severity === 'warning') ? 'warning' : undefined;
  const origin = field.origin === 'environment' ? 'Set by the environment file'
    : field.origin === 'compose' ? (field.composeVariable ? `Set by Compose from .env ${field.composeVariable}` : 'Set by docker-compose.yml')
      : field.origin === 'option' && !field.added ? 'Not in appsettings.json: the option default applies' : undefined;
  return <div id={`${id}-row`} className="cb-row" data-compact={compact || undefined} data-changed={changed || undefined} data-issue={severity}>
    <div className="cb-row-text">
      {isGroupControl(field)
        ? <span id={`${id}-label`} className="cb-row-label">{label ?? field.label}{changed && <span className="sr-only"> (changed)</span>}</span>
        : <label id={`${id}-label`} htmlFor={id} className="cb-row-label">{label ?? field.label}{changed && <span className="sr-only"> (changed)</span>}</label>}
      <span className="cb-key-line">
        <code className="cb-key">{field.path}</code>
        {field.guide && <Link href={field.guide} className="cb-doc-link" aria-label={`Docs for ${field.path}`}><BookOpen aria-hidden="true" />Docs</Link>}
        {more}
      </span>
      <div id={describedBy} className="cb-meaning">
        {!compact && field.description && <p>{renderInline(field.description)}</p>}
        {field.hint && !compact && <p className="cb-hint">{field.hint}</p>}
        {origin && !compact && <p className="cb-origin">{origin}.</p>}
        {issues.map((issue) => <p key={issue.id} className="cb-row-issue" data-severity={issue.severity}>{issue.title}</p>)}
      </div>
    </div>
    <div className="cb-row-control">
      <FieldControl field={field} value={value} controlId={id} onChange={(next) => onChange(field, next)} describedBy={describedBy} secretNote={field.secret ? secretNote(field) : undefined} />
      {changed && <div className="cb-row-foot">
        {!field.secret && changed && !field.added && <>
          <span className="cb-default">Default <span className="cb-default-value">{field.unit === 'bytes' && typeof field.defaultValue === 'number' ? formatBytes(field.defaultValue) : display(field.defaultValue)}</span></span>
          <button type="button" className="cb-tool cb-tool-quiet" onClick={() => onReset(field)} aria-label={`Reset ${field.path} to its default`}><RotateCcw aria-hidden="true" />Reset</button>
        </>}
      </div>}
    </div>
  </div>;
}

/** `code` spans of a description, as written in the reference tables. */
function renderInline(text: string) {
  return text.split(/(`[^`]+`)/g).map((part, index) => (part.startsWith('`') && part.endsWith('`') ? <code key={index}>{part.slice(1, -1)}</code> : part));
}

function AddEntry({ dictionary, entriesOf, onAddEntry }: Props & { dictionary: string }) {
  const definition = dictionaries[dictionary as keyof typeof dictionaries];
  const [name, setName] = useState('');
  const [error, setError] = useState('');
  const id = useId();
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = name.trim();
    if (!/^[A-Za-z][\w-]*$/.test(trimmed)) { setError('Use letters, digits, - or _, starting with a letter.'); return; }
    if (entriesOf(dictionary).some((entry) => entry.toLowerCase() === trimmed.toLowerCase())) { setError(`A ${definition.label} named ${trimmed} exists.`); return; }
    onAddEntry(dictionary, trimmed);
    setName('');
    setError('');
  };
  return <form className="cb-add" onSubmit={submit}>
    <label htmlFor={id}>Add a {definition.label}</label>
    <div className="cb-inline">
      <input id={id} className="cb-input cb-input-short" value={name} placeholder={definition.placeholder} onChange={(event) => setName(event.target.value)} aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} spellCheck={false} autoComplete="off" />
      <button type="submit" className="cb-tool"><Plus aria-hidden="true" />Add</button>
    </div>
    {error && <p id={`${id}-error`} className="cb-error" role="alert">{error}</p>}
  </form>;
}
