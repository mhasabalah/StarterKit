'use client';
import { useId, useState, type KeyboardEvent } from 'react';
import { ArrowDown, ArrowUp, KeyRound, X } from 'lucide-react';
import { describeCron, describeTimespan, formatBytes, fieldId } from '@/lib/config-builder/format.mjs';
import type { Field } from './types';

type Props = {
  field: Field;
  value: unknown;
  onChange: (value: unknown) => void;
  describedBy: string;
  secretNote?: string;
  /** DOM id of the control; defaults to the field's own (a second copy of a field needs another). */
  controlId?: string;
};

const isSegmented = (field: Field) => {
  const options = field.options ?? [];
  return options.length > 0 && options.length <= 4 && options.every((option) => (option.label ?? option.value).length <= 18);
};

/** Controls that are a group (radios, checkboxes, a list): labelled with aria-labelledby, not a <label for>. */
export function isGroupControl(field: Field) {
  return field.control === 'secret' || field.control === 'multi' || field.control === 'ordered' || (field.control === 'enum' && isSegmented(field));
}

/** One control per kind of setting. Every control is a native element, labelled by the row's label. */
export function FieldControl({ field, value, onChange, describedBy, secretNote, controlId }: Props) {
  const id = controlId ?? fieldId(field.path);
  switch (field.control) {
    case 'secret': return <SecretNote id={id} note={secretNote} describedBy={describedBy} />;
    case 'boolean': return <label className="cb-switch">
      <input id={id} type="checkbox" role="switch" checked={value === true || value === 'true'} aria-describedby={describedBy} onChange={(event) => onChange(event.target.checked)} />
      <span className="cb-switch-track" aria-hidden="true" /><span className="cb-switch-label">{value === true || value === 'true' ? 'On' : 'Off'}</span>
    </label>;
    case 'enum': return <EnumControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
    case 'multi': return <MultiControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
    case 'ordered': return <OrderedControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
    case 'tags': return <TagsControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
    case 'number': return <NumberControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
    case 'color': return <div className="cb-inline">
      <input type="color" className="cb-color" aria-label={`${field.label} colour`} value={/^#[0-9a-f]{6}$/i.test(String(value)) ? String(value) : '#000000'} onChange={(event) => onChange(event.target.value)} />
      <input id={id} className="cb-input cb-input-short" value={String(value ?? '')} aria-describedby={describedBy} onChange={(event) => onChange(event.target.value)} spellCheck={false} />
    </div>;
    default: return <TextControl id={id} field={field} value={value} onChange={onChange} describedBy={describedBy} />;
  }
}

function SecretNote({ id, note, describedBy }: { id: string; note?: string; describedBy: string }) {
  return <p id={id} className="cb-secret" tabIndex={-1} aria-describedby={describedBy}><KeyRound aria-hidden="true" /><span>{note ?? 'Supplied as a secret, never written to a file.'}</span></p>;
}

function TextControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const text = value === null || value === undefined ? '' : String(value);
  const reading = field.control === 'cron' ? describeCron(text) : field.control === 'timespan' ? describeTimespan(text) : undefined;
  const nullable = field.defaultValue === null;
  return <div className="cb-stack">
    <input id={id} className="cb-input" value={text} placeholder={nullable ? 'Not set' : undefined} aria-describedby={describedBy} spellCheck={false} autoComplete="off"
      inputMode={field.control === 'timespan' ? 'numeric' : undefined}
      onChange={(event) => onChange(nullable && event.target.value === '' ? null : event.target.value)} />
    {reading && <span className="cb-reading">{reading}</span>}
    {field.control === 'cron' && !reading && text && <span className="cb-reading">Cron, UTC: minute hour day month weekday</span>}
  </div>;
}

function NumberControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const number = typeof value === 'number' ? value : Number(value);
  return <div className="cb-inline">
    <input id={id} className="cb-input cb-input-number" type="number" value={Number.isFinite(number) ? number : ''} min={field.min} max={field.max} step={field.step ?? (Number.isInteger(field.defaultValue) ? 1 : 'any')}
      aria-describedby={describedBy} onChange={(event) => { if (event.target.value !== '') onChange(Number(event.target.value)); }} />
    {field.unit && <span className="cb-unit">{field.unit === 'bytes' ? formatBytes(number) || 'bytes' : field.unit}</span>}
  </div>;
}

function EnumControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const current = value === null || value === undefined ? '' : String(value);
  const options = [...(field.options ?? [])];
  if (current && !options.some((option) => option.value === current)) options.push({ value: current, label: `${current} (custom)` });
  const selected = options.find((option) => option.value === current);
  if (isSegmented(field)) return <div className="cb-stack">
    <div className="cb-segmented" role="radiogroup" aria-labelledby={`${id}-label`} aria-describedby={describedBy}>
      {options.map((option, index) => <label key={option.value} className="cb-segment">
        <input type="radio" id={index === 0 ? id : undefined} name={id} value={option.value} checked={option.value === current} onChange={() => onChange(option.value)} />
        <span>{option.label ?? option.value}</span>
      </label>)}
    </div>
    {selected?.hint && <span className="cb-reading">{selected.hint}</span>}
  </div>;
  return <div className="cb-stack">
    <select id={id} className="cb-input cb-select" value={current} aria-describedby={describedBy} onChange={(event) => onChange(event.target.value)}>
      {!options.length && <option value="">No entries yet</option>}
      {options.map((option) => <option key={option.value} value={option.value}>{option.label ?? (option.value || 'Default')}</option>)}
    </select>
    {selected?.hint && <span className="cb-reading">{selected.hint}</span>}
  </div>;
}

const asList = (value: unknown, format?: 'array' | 'csv') => (format === 'csv'
  ? String(value ?? '').split(',').map((item) => item.trim()).filter(Boolean)
  : Array.isArray(value) ? value.map(String) : []);

function MultiControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const list = asList(value, field.format);
  const options = [...(field.options ?? [])];
  for (const item of list) if (!options.some((option) => option.value === item)) options.push({ value: item });
  const toggle = (item: string, on: boolean) => {
    const next = options.map((option) => option.value).filter((name) => (name === item ? on : list.includes(name)));
    onChange(field.format === 'csv' ? next.join(',') : next);
  };
  return <fieldset className="cb-checks" aria-labelledby={`${id}-label`} aria-describedby={describedBy}>
    {options.map((option, index) => <label key={option.value} className="cb-check">
      <input type="checkbox" id={index === 0 ? id : undefined} checked={list.includes(option.value)} onChange={(event) => toggle(option.value, event.target.checked)} />
      <span>{option.label ?? option.value}</span>
    </label>)}
  </fieldset>;
}

function OrderedControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const list = asList(value);
  const [announcement, setAnnouncement] = useState('');
  const rest = (field.options ?? []).map((option) => option.value).filter((name) => !list.includes(name));
  const label = (name: string) => field.options?.find((option) => option.value === name)?.label ?? name;
  const move = (index: number, by: number) => {
    const next = [...list];
    const [item] = next.splice(index, 1);
    next.splice(index + by, 0, item);
    onChange(next);
    setAnnouncement(`${label(item)} moved to position ${index + by + 1} of ${next.length}`);
  };
  return <div className="cb-ordered" aria-describedby={describedBy}>
    <ol aria-labelledby={`${id}-label`}>
      {list.map((name, index) => <li key={name}>
        <span className="cb-ordered-position" aria-hidden="true">{index + 1}</span>
        <span className="cb-ordered-name">{label(name)}</span>
        <button type="button" id={index === 0 ? id : undefined} className="cb-icon-button" aria-label={`Move ${label(name)} up`} disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp aria-hidden="true" /></button>
        <button type="button" className="cb-icon-button" aria-label={`Move ${label(name)} down`} disabled={index === list.length - 1} onClick={() => move(index, 1)}><ArrowDown aria-hidden="true" /></button>
        <button type="button" className="cb-icon-button" aria-label={`Remove ${label(name)}`} disabled={list.length === 1} onClick={() => { onChange(list.filter((item) => item !== name)); setAnnouncement(`${label(name)} removed`); }}><X aria-hidden="true" /></button>
      </li>)}
    </ol>
    {rest.length > 0 && <div className="cb-ordered-add">{rest.map((name) => <button key={name} type="button" className="cb-chip-button" onClick={() => { onChange([...list, name]); setAnnouncement(`${label(name)} added last`); }}>Add {label(name)}</button>)}</div>}
    <span className="sr-only" role="status">{announcement}</span>
  </div>;
}

function TagsControl({ id, field, value, onChange, describedBy }: Omit<Props, 'secretNote'> & { id: string }) {
  const list = Array.isArray(value) ? value : [];
  const numeric = Array.isArray(field.defaultValue) && field.defaultValue.length > 0 && field.defaultValue.every((item) => typeof item === 'number');
  const [draft, setDraft] = useState('');
  const hint = useId();
  const commit = () => {
    const items = draft.split(',').map((item) => item.trim()).filter(Boolean).map((item) => (numeric && !Number.isNaN(Number(item)) ? Number(item) : item));
    const fresh = items.filter((item) => !list.includes(item));
    if (fresh.length) onChange([...list, ...fresh]);
    setDraft('');
  };
  const onKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') { event.preventDefault(); commit(); }
    if (event.key === 'Backspace' && !draft && list.length) onChange(list.slice(0, -1));
  };
  return <div className="cb-tags">
    {list.length > 0 && <ul className="cb-tag-list" aria-label={`${field.label}: ${list.length} ${list.length === 1 ? 'entry' : 'entries'}`}>
      {list.map((item, index) => <li key={`${String(item)}-${index}`} className="cb-tag"><span>{String(item)}</span>
        <button type="button" aria-label={`Remove ${String(item)}`} onClick={() => onChange(list.filter((_, position) => position !== index))}><X aria-hidden="true" /></button>
      </li>)}
    </ul>}
    <input id={id} className="cb-input" value={draft} placeholder={list.length ? 'Add another' : 'Empty. Type and press Enter'} aria-describedby={`${describedBy} ${hint}`} spellCheck={false} autoComplete="off"
      onChange={(event) => setDraft(event.target.value)} onKeyDown={onKey} onBlur={commit} />
    <span id={hint} className="sr-only">Press Enter or comma to add. Backspace in an empty box removes the last entry.</span>
  </div>;
}
