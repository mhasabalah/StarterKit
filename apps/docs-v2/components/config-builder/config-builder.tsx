'use client';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { FileUp, Link2, RotateCcw, X } from 'lucide-react';
import {
  initialState, createContext, buildFields, lookup, isVisible, isChanged, valueOf, withValue, addEntry, removeEntry,
  dictionaryEntries, buildEnvironmentFile, buildFullFile, buildEnvironmentVariables, buildSecrets, encodeState,
  decodeState, sanitizeState, importSettings, moduleOfSection,
} from '@/lib/config-builder/model.mjs';
import { evaluateRules } from '@/lib/config-builder/rules.mjs';
import { environments, modules } from '@/lib/config-builder/overlay.mjs';
import { fieldId, sectionId } from '@/lib/config-builder/format.mjs';
import { ProductPanel } from './product-panel';
import { SettingsPanel, type Row, type SectionView } from './settings-panel';
import { OutputPanel, type Outputs } from './output-panel';
import { CopyButton } from './code-view';
import type { Catalog, Field, Issue, State } from './types';
import './config-builder.css';

const storageKey = 'starterkit-config-builder:v1';
type Pane = 'product' | 'settings' | 'output';

/**
 * The configuration builder: choose where the API runs and which modules the product has, change what differs, and
 * export it. Values stay in the browser (local storage and the share link); secrets are never taken.
 */
export function ConfigBuilder({ catalog }: { catalog: Catalog }) {
  const [state, setState] = useState<State>(() => initialState(catalog) as State);
  const [productName, setProductName] = useState('MyProduct');
  const [query, setQuery] = useState('');
  const [showAll, setShowAll] = useState(false);
  const [changedOnly, setChangedOnly] = useState(false);
  const [pane, setPane] = useState<Pane>('settings');
  const [ready, setReady] = useState(false);
  const [notice, setNotice] = useState('');
  const [importing, setImporting] = useState(false);
  const [confirmReset, setConfirmReset] = useState(false);
  const [jump, setJump] = useState<string | null>(null);
  const [activeSection, setActiveSection] = useState<string | null>(null);

  const context = useMemo(() => createContext(catalog, state.symbols, state.environment), [catalog, state.symbols, state.environment]);
  const fields = useMemo(() => buildFields(context, state) as unknown as Field[], [context, state]);
  const byPath = useMemo(() => lookup(fields) as Map<string, Field>, [fields]);
  const issues = useMemo(() => evaluateRules(context, fields as never, state) as Issue[], [context, fields, state]);
  const issuesByPath = useMemo(() => {
    const map = new Map<string, Issue[]>();
    for (const issue of issues) for (const path of issue.paths) if (issue.id !== 'secrets') map.set(path, [...(map.get(path) ?? []), issue]);
    return map;
  }, [issues]);

  // Restore: a share link wins over the saved state. Then jump to a #section= deep link.
  useEffect(() => {
    const hash = new URLSearchParams(window.location.hash.slice(1));
    let restored: State | null = null;
    let name: string | undefined;
    const shared = hash.get('s');
    if (shared) restored = decodeState(catalog, shared) as State | null;
    if (!restored) {
      try {
        const saved = JSON.parse(window.localStorage.getItem(storageKey) ?? 'null');
        if (saved?.state) restored = decodeState(catalog, encodeState(saved.state)) as State | null;
        if (typeof saved?.productName === 'string') name = saved.productName;
      } catch { /* a corrupt entry starts fresh */ }
    }
    if (restored) {
      const restoredFields = buildFields(createContext(catalog, restored.symbols, restored.environment), restored);
      // eslint-disable-next-line react-hooks/set-state-in-effect -- restoring client-only storage after hydration
      setState(sanitizeState(restoredFields, restored) as State);
      if (shared) setNotice('Loaded the configuration from the link.');
    }
    if (name) setProductName(name);
    const section = hash.get('section');
    if (section) { setActiveSection(section); setJump(`section:${section}`); }
    setReady(true);
  }, [catalog]);

  // A #section= link followed while the page is already open.
  useEffect(() => {
    const onHash = () => {
      const section = new URLSearchParams(window.location.hash.slice(1)).get('section');
      if (section) { setPane('settings'); setActiveSection(section); setJump(`section:${section}`); }
    };
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  useEffect(() => {
    if (!ready) return;
    try { window.localStorage.setItem(storageKey, JSON.stringify({ state, productName })); } catch { /* storage full or blocked */ }
  }, [state, productName, ready]);

  // Scroll to (and focus) a field or section once it is rendered.
  // One tick later, so the router's own scroll on navigation (a deep link) has already happened.
  useEffect(() => {
    if (!jump) return;
    const handle = window.setTimeout(() => {
      const target = jump.startsWith('section:') ? document.getElementById(sectionId(jump.slice(8))) : document.getElementById(`${fieldId(jump)}-row`) ?? document.getElementById(fieldId(jump));
      if (!target) return;
      target.scrollIntoView({ block: 'start', behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
      const focusable = jump.startsWith('section:') ? target.querySelector('h3') : document.getElementById(fieldId(jump));
      if (focusable instanceof HTMLElement) { if (focusable.tagName === 'H3') focusable.tabIndex = -1; focusable.focus({ preventScroll: true }); }
      setJump(null);
    }, 60);
    return () => window.clearTimeout(handle);
  }, [jump, fields]);

  const sections = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const views = new Map<string, SectionView>();
    let total = 0;
    for (const field of fields) {
      if (!isVisible(field, byPath, state)) continue;
      total++;
      const changed = isChanged(field, state);
      const matches = !needle || field.path.toLowerCase().includes(needle) || field.label.toLowerCase().includes(needle) || field.description.toLowerCase().includes(needle);
      const prominent = showAll || field.essential || changed || issuesByPath.has(field.path) || Boolean(field.added);
      const shown = (needle ? matches : prominent) && (!changedOnly || changed);
      const view = views.get(field.section) ?? { section: field.section, moduleId: moduleOfSection(field.section)?.id ?? 'Platform', rows: [] as Row[], hidden: 0, changes: 0 };
      if (changed) view.changes++;
      if (shown) view.rows.push({ field, value: valueOf(field, state), changed });
      else if (!needle && !changedOnly) view.hidden++;
      views.set(field.section, view);
    }
    return { list: [...views.values()].filter((view) => view.rows.length > 0), all: [...views.values()], total };
  }, [fields, byPath, state, query, showAll, changedOnly, issuesByPath]);

  const outputs = useMemo((): Outputs => {
    const environment = environments.find((entry) => entry.id === state.environment) ?? environments[0];
    return {
      environment: { id: environment.id, label: environment.label, relaxed: environment.relaxed },
      environmentFile: buildEnvironmentFile(context, fields as never, state) as unknown as Outputs['environmentFile'],
      changesOnlyFile: buildEnvironmentFile(context, fields as never, state, { onlyChanges: true }) as unknown as Outputs['changesOnlyFile'],
      fullFile: buildFullFile(context, fields as never, state) as unknown as Outputs['fullFile'],
      variables: buildEnvironmentVariables(context, fields as never, state) as unknown as Outputs['variables'],
      secrets: buildSecrets(context, fields as never, state) as unknown as Outputs['secrets'],
    };
  }, [context, fields, state]);

  const secretNote = useCallback((field: Field) => {
    if (state.environment === 'Development') return `Set it with dotnet user-secrets set "${field.path}".`;
    if (state.environment === 'Production') return `Set ${field.path.split(':').join('__')} from your secret store.`;
    if (field.composeVariable) return `Set ${field.composeVariable} in .env: Compose passes it to the API.`;
    const mountedBy = catalog.composeSecrets[field.secretFile ?? ''];
    const file = `secrets/${field.secretFile}.txt`;
    if (mountedBy === 'docker-compose.yml') return `Supplied by ${file}, mounted by docker-compose.yml.`;
    if (mountedBy) return `Supplied by ${file}, mounted by ${mountedBy} (add it with -f).`;
    return `Supplied by ${file}. Mount it with the snippet in Secrets.`;
  }, [catalog, state.environment]);

  const update = (next: State) => { setState(next); setConfirmReset(false); };
  const onChange = (field: Field, value: unknown) => update(withValue(state, field, value) as State);
  const onReset = (field: Field) => update(withValue(state, field, field.defaultValue) as State);
  const onSymbol = (symbol: string, value: string | boolean) => update({ ...state, symbols: { ...state.symbols, [symbol]: value } });
  const onEnvironment = (environment: string) => update({ ...state, environment });
  const valueAt = useCallback((path: string) => { const field = byPath.get(path); return field ? valueOf(field, state) : undefined; }, [byPath, state]);
  const changeCount = fields.filter((field) => isChanged(field, state)).length + state.removed.length;
  const blocking = issues.filter((issue) => issue.severity === 'blocking').length;

  const onJump = (path: string) => {
    const field = byPath.get(path);
    setPane('settings');
    setQuery('');
    setChangedOnly(false);
    if (field && !field.essential) setShowAll(true);
    if (field && !isVisible(field, byPath, state)) { setNotice(`${path} applies only when ${field.visibleWhen.map((condition) => condition.path).join(' and ')} enables it.`); setJump(field.visibleWhen[0]?.path ?? null); return; }
    setJump(path);
  };

  const share = () => {
    const url = `${window.location.origin}${window.location.pathname}#s=${encodeState(state)}`;
    window.history.replaceState(null, '', url);
    return url;
  };

  return <div className="cb" data-pane={pane}>
    <header className="cb-head">
      <div className="cb-head-text">
        <h1>Configuration builder</h1>
        <p>Pick where the API runs and which modules your product has, change what differs, and export appsettings, environment variables and the secrets list. Every key links to its documentation. Nothing leaves your browser, and secrets are never asked for.</p>
      </div>
      <div className="cb-head-actions">
        <span className="cb-head-count" aria-live="polite">{changeCount} change{changeCount === 1 ? '' : 's'}</span>
        <CopyButton text={share} label="Copy a link to this configuration (secrets are never included)"><Link2 aria-hidden="true" />Copy share link</CopyButton>
        <button type="button" className="cb-tool" aria-expanded={importing} aria-controls="cb-import" onClick={() => setImporting(!importing)}><FileUp aria-hidden="true" />Import</button>
        {confirmReset
          ? <span className="cb-confirm" role="group" aria-label="Confirm reset">
            <span>Discard {changeCount} change{changeCount === 1 ? '' : 's'}?</span>
            <button type="button" className="cb-tool cb-tool-danger" onClick={() => { setState(initialState(catalog) as State); setConfirmReset(false); setNotice('Every setting is back to its default.'); window.history.replaceState(null, '', window.location.pathname); }}>Discard</button>
            <button type="button" className="cb-tool" onClick={() => setConfirmReset(false)}>Keep</button>
          </span>
          : <button type="button" className="cb-tool" disabled={changeCount === 0} onClick={() => setConfirmReset(true)}><RotateCcw aria-hidden="true" />Reset all</button>}
      </div>
      {importing && <ImportPanel onClose={() => setImporting(false)} onImport={(text) => {
        const result = importSettings(context, fields as never, state, text);
        update(result.state as State);
        setImporting(false);
        setChangedOnly(true);
        setNotice(`Imported ${result.imported} change${result.imported === 1 ? '' : 's'}.${result.skippedSecrets.length ? ` Skipped ${result.skippedSecrets.length} secret${result.skippedSecrets.length === 1 ? '' : 's'}.` : ''}${result.unknown.length ? ` Unknown keys left out: ${result.unknown.slice(0, 5).join(', ')}${result.unknown.length > 5 ? '…' : ''}.` : ''}`);
      }} />}
      {notice && <p className="cb-notice" role="status">{notice}<button type="button" className="cb-icon-button" aria-label="Dismiss" onClick={() => setNotice('')}><X aria-hidden="true" /></button></p>}
    </header>

    <nav className="cb-panes" aria-label="Builder panels">
      {([['product', 'Product'], ['settings', 'Settings'], ['output', blocking ? `Export (${blocking} blocking)` : 'Export']] as [Pane, string][]).map(([id, label]) => <button key={id} type="button" className="cb-pane-tab" aria-pressed={pane === id} onClick={() => setPane(id)}>{label}</button>)}
    </nav>

    <div className="cb-grid">
      <aside className="cb-pane cb-pane-product" aria-label="Your product">
        <ProductPanel catalog={catalog} state={state} productName={productName} onProductName={setProductName} onSymbol={onSymbol} onEnvironment={onEnvironment} />
        <SectionNav sections={sections.all} issuesByPath={issuesByPath} active={activeSection} onPick={(section) => { setPane('settings'); setActiveSection(section); setJump(`section:${section}`); }} />
      </aside>
      <main className="cb-pane cb-pane-settings" aria-label="Settings">
        <SettingsPanel catalog={catalog} sections={sections.list} query={query} onQuery={setQuery} showAll={showAll} onShowAll={setShowAll} changedOnly={changedOnly} onChangedOnly={setChangedOnly}
          totalVisible={sections.total} issuesByPath={issuesByPath} secretNote={secretNote} valueAt={valueAt} onChange={onChange} onReset={onReset}
          onAddEntry={(dictionary, name) => update(addEntry(state, dictionary, name) as State)} onRemoveEntry={(dictionary, name) => update(removeEntry(state, dictionary, name) as State)}
          entriesOf={(dictionary) => dictionaryEntries(context, state, dictionary) as string[]} />
      </main>
      <aside className="cb-pane cb-pane-output" aria-label="Checks and export">
        <OutputPanel outputs={outputs} issues={issues} onJump={onJump} secretNote={secretNote} />
      </aside>
    </div>
  </div>;
}

function SectionNav({ sections, issuesByPath, active, onPick }: { sections: SectionView[]; issuesByPath: Map<string, Issue[]>; active: string | null; onPick: (section: string) => void }) {
  const issueCount = (section: string) => [...issuesByPath.keys()].filter((path) => path.split(':')[0] === section).length;
  return <nav className="cb-block cb-nav" aria-label="Sections">
    <h2 className="cb-block-title">Sections</h2>
    {modules.map((module) => {
      const own = sections.filter((section) => section.moduleId === module.id);
      if (!own.length) return null;
      return <div key={module.id} className="cb-nav-group" data-family={module.family}>
        <p className="cb-nav-module"><span className="cb-chip" aria-hidden="true">{module.symbol}</span>{module.name}</p>
        <ul>{own.map((section) => {
          const issues = issueCount(section.section);
          return <li key={section.section}><button type="button" className="cb-nav-link" aria-current={active === section.section ? 'true' : undefined} onClick={() => onPick(section.section)}>
            <span>{section.section}</span>
            <span className="cb-nav-counts">
              {issues > 0 && <span className="cb-nav-issue" aria-label={`${issues} with checks`}>{issues}</span>}
              {section.changes > 0 && <span className="cb-nav-change" aria-label={`${section.changes} changed`}>{section.changes}</span>}
            </span>
          </button></li>;
        })}</ul>
      </div>;
    })}
  </nav>;
}

function ImportPanel({ onImport, onClose }: { onImport: (text: string) => void; onClose: () => void }) {
  const [text, setText] = useState('');
  const [error, setError] = useState('');
  const id = useId();
  const area = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { area.current?.focus(); }, []);
  const load = (value: string) => {
    try { onImport(value); setError(''); } catch (problem) { setError(problem instanceof Error ? problem.message : 'The file could not be read.'); }
  };
  return <div id="cb-import" className="cb-import">
    <label htmlFor={id} className="cb-block-title">Import an appsettings file</label>
    <p className="cb-reading">Paste appsettings.json or an environment file, or choose one. Known keys become changes against the current environment. Secret values are skipped and never stored.</p>
    <textarea id={id} ref={area} className="cb-input cb-textarea" rows={8} value={text} onChange={(event) => setText(event.target.value)} spellCheck={false} placeholder={'{\n  "Messaging": { "Transport": "PostgreSql" }\n}'} aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} />
    {error && <p id={`${id}-error`} className="cb-error" role="alert">{error}</p>}
    <div className="cb-inline">
      <button type="button" className="cb-tool cb-tool-primary" disabled={!text.trim()} onClick={() => load(text)}>Import</button>
      <label className="cb-tool cb-file"><input type="file" accept=".json,application/json" onChange={async (event) => { const file = event.target.files?.[0]; if (file) load(await file.text()); }} />Choose a file</label>
      <button type="button" className="cb-tool cb-tool-quiet" onClick={onClose}>Cancel</button>
    </div>
  </div>;
}
