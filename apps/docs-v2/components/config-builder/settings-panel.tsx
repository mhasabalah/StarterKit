'use client';
import Link from 'next/link';
import { Fragment, useId, useState, type FormEvent } from 'react';
import { BookOpen, Plus, RotateCcw, Search, Trash2 } from 'lucide-react';
import { modules, dictionaries } from '@/lib/config-builder/overlay.mjs';
import { humanize } from '@/lib/config-builder/model.mjs';
import { fieldId, sectionId, formatBytes } from '@/lib/config-builder/format.mjs';
import { FieldControl, isGroupControl } from './field-control';
import type { Catalog, Field, Issue } from './types';

export type Row = { field: Field; value: unknown; changed: boolean };
export type SectionView = { section: string; moduleId: string; rows: Row[]; hidden: number; changes: number };

type Props = {
  catalog: Catalog;
  sections: SectionView[];
  query: string;
  onQuery: (query: string) => void;
  showAll: boolean;
  onShowAll: (value: boolean) => void;
  changedOnly: boolean;
  onChangedOnly: (value: boolean) => void;
  totalVisible: number;
  issuesByPath: Map<string, Issue[]>;
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

/** The settings, section by section, in module startup order. */
export function SettingsPanel(props: Props) {
  const { sections, query, onQuery, showAll, onShowAll, changedOnly, onChangedOnly, totalVisible } = props;
  const filterId = useId();
  const shown = sections.reduce((sum, section) => sum + section.rows.length, 0);
  return <div className="cb-settings">
    <div className="cb-toolbar" role="search">
      <label htmlFor={filterId} className="sr-only">Filter settings</label>
      <div className="cb-filter"><Search aria-hidden="true" />
        <input id={filterId} type="search" value={query} placeholder={`Filter ${totalVisible} settings`} title="Matches the key, the name and the meaning" onChange={(event) => onQuery(event.target.value)} autoComplete="off" spellCheck={false} />
      </div>
      <div className="cb-toggles">
        <label className="cb-check"><input type="checkbox" checked={showAll} onChange={(event) => onShowAll(event.target.checked)} /><span>All settings</span></label>
        <label className="cb-check"><input type="checkbox" checked={changedOnly} onChange={(event) => onChangedOnly(event.target.checked)} /><span>Changed only</span></label>
      </div>
      <p className="cb-toolbar-count" role="status" aria-live="polite">{shown} shown{!showAll && !query ? ' (the settings most products change)' : ''}</p>
    </div>
    {sections.length === 0 && <div className="cb-empty">
      <p>{changedOnly ? 'Nothing is changed yet. Every value is the shipped default.' : `No setting matches "${query}".`}</p>
      <button type="button" className="cb-tool" onClick={() => { onQuery(''); onChangedOnly(false); }}>Show every setting</button>
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
  const { catalog, view, valueAt, onRemoveEntry } = props;
  const meta = catalog.sections[view.section];
  const groups: { group: string; rows: Row[] }[] = [];
  for (const row of view.rows) {
    const last = groups.at(-1);
    if (last && last.group === row.field.group) last.rows.push(row);
    else groups.push({ group: row.field.group, rows: [row] });
  }
  // Add forms go after the last group of each dictionary in this section (or at the end when it has no entries).
  const sectionDictionaries = Object.keys(dictionaries).filter((path) => path.split(':')[0] === view.section);
  const lastGroupOf = new Map(sectionDictionaries.map((path) => [path, groups.findLastIndex((group) => group.group.startsWith(`${path}:`))]));
  return <section id={sectionId(view.section)} className="cb-section" aria-labelledby={`${sectionId(view.section)}-title`} data-section={view.section}>
    <header className="cb-section-head">
      <h3 id={`${sectionId(view.section)}-title`}><code>{view.section}</code></h3>
      <span className="cb-section-meta">
        {view.changes > 0 && <span className="cb-count">{view.changes} changed</span>}
        {view.hidden > 0 && <span className="cb-muted">{view.hidden} more under All settings</span>}
        {meta?.anchor && <Link href={meta.anchor} className="cb-doc-link"><BookOpen aria-hidden="true" />Reference</Link>}
        {meta?.guide && meta.guide !== meta.anchor && <Link href={meta.guide} className="cb-doc-link">Guide</Link>}
      </span>
    </header>
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
    {sectionDictionaries.filter((path) => lastGroupOf.get(path) === -1 && (props.showAll || props.query === '')).map((path) => <AddEntry key={path} {...props} dictionary={path} />)}
  </section>;
}

function FieldRow({ row, issuesByPath, secretNote, onChange, onReset }: Props & { row: Row }) {
  const { field, value, changed } = row;
  const id = fieldId(field.path);
  const describedBy = `${id}-meaning`;
  const issues = issuesByPath.get(field.path) ?? [];
  const severity = issues.some((issue) => issue.severity === 'blocking') ? 'blocking' : issues.some((issue) => issue.severity === 'warning') ? 'warning' : undefined;
  const origin = field.origin === 'environment' ? 'Set by the environment file'
    : field.origin === 'compose' ? (field.composeVariable ? `Set by Compose from .env ${field.composeVariable}` : 'Set by docker-compose.yml')
      : field.origin === 'option' && !field.added ? 'Not in appsettings.json: the option default applies' : undefined;
  return <div id={`${id}-row`} className="cb-row" data-changed={changed || undefined} data-issue={severity}>
    <div className="cb-row-text">
      {isGroupControl(field)
        ? <span id={`${id}-label`} className="cb-row-label">{field.label}</span>
        : <label id={`${id}-label`} htmlFor={id} className="cb-row-label">{field.label}</label>}
      <code className="cb-key">{field.path}</code>
      <div id={describedBy} className="cb-meaning">
        {field.description && <p>{renderInline(field.description)}</p>}
        {field.hint && <p className="cb-hint">{field.hint}</p>}
        {origin && <p className="cb-origin">{origin}.</p>}
        {issues.map((issue) => <p key={issue.id} className="cb-row-issue" data-severity={issue.severity}>{issue.title}</p>)}
      </div>
    </div>
    <div className="cb-row-control">
      <FieldControl field={field} value={value} onChange={(next) => onChange(field, next)} describedBy={describedBy} secretNote={field.secret ? secretNote(field) : undefined} />
      <div className="cb-row-foot">
        {!field.secret && changed && !field.added && <>
          <span className="cb-default">Default: <span className="cb-default-value">{field.unit === 'bytes' && typeof field.defaultValue === 'number' ? formatBytes(field.defaultValue) : display(field.defaultValue)}</span></span>
          <button type="button" className="cb-tool cb-tool-quiet" onClick={() => onReset(field)} aria-label={`Reset ${field.path} to its default`}><RotateCcw aria-hidden="true" />Reset</button>
        </>}
        {field.guide && <Link href={field.guide} className="cb-doc-link" aria-label={`Docs for ${field.path}`}><BookOpen aria-hidden="true" />Docs</Link>}
      </div>
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
