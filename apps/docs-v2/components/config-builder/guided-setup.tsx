'use client';
import Link from 'next/link';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
  AlertTriangle, ArrowLeft, ArrowRight, BookOpen, Bot, Check, ChevronRight, CircleCheck, Container, Copy, Download, Laptop, OctagonX,
  RotateCcw, Server, SlidersHorizontal, Terminal, type LucideIcon,
} from 'lucide-react';
import { guideSteps, guidePaths, stepKeys, handover, applyScript, questionHelp } from '@/lib/config-builder/guide.mjs';
import { decisionLabels, decisions, environments, modules } from '@/lib/config-builder/overlay.mjs';
import { dotnetNewCommand, isModuleIncluded, isVisible, isChanged, valueOf } from '@/lib/config-builder/model.mjs';
import { fieldId } from '@/lib/config-builder/format.mjs';
import { configPrompt } from '@/lib/agent-prompt.mjs';
import { basePath } from '@/lib/site';
import { ModulePicker } from './product-panel';
import { FieldControl, isGroupControl } from './field-control';
import { CodeView, download } from './code-view';
import type { Outputs } from './output-panel';
import type { Catalog, Field, Issue, Option, State } from './types';

export type GuidePath = 'agent' | 'manual';

type Props = {
  catalog: Catalog;
  state: State;
  byPath: Map<string, Field>;
  issues: Issue[];
  issuesByPath: Map<string, Issue[]>;
  outputs: Outputs;
  productName: string;
  path: GuidePath | null;
  step: number;
  onPath: (path: GuidePath) => void;
  onStep: (step: number) => void;
  onProductName: (name: string) => void;
  onSymbol: (symbol: string, value: string | boolean) => void;
  onEnvironment: (environment: string) => void;
  onChange: (field: Field, value: unknown) => void;
  onReset: (field: Field) => void;
  secretNote: (field: Field) => string;
  onOpenBuilder: () => void;
};

const last = guideSteps.length - 1;
const pathIcon: Record<GuidePath, LucideIcon> = { agent: Bot, manual: Terminal };
const environmentIcon: Record<string, LucideIcon> = { Development: Laptop, Docker: Container, Production: Server };
const labelOf = (field: Field) => decisionLabels[field.path as keyof typeof decisionLabels] ?? field.label;
const plural = (count: number, one: string, many = `${one}s`) => `${count} ${count === 1 ? one : many}`;

/** The first sentence of a reference description: the question card's one line of help. */
function helpOf(field: Field) {
  const plain = questionHelp[field.path as keyof typeof questionHelp];
  if (plain) return plain;
  if (field.hint) return field.hint;
  const text = field.description.replace(/\s+/g, ' ').trim();
  const end = text.search(/\.\s/);
  return end > 0 && end < 200 ? text.slice(0, end + 1) : text;
}
const inline = (text: string) => text.split(/(`[^`]+`)/g).map((part, index) => (part.startsWith('`') && part.endsWith('`') ? <code key={index}>{part.slice(1, -1)}</code> : part));

/**
 * The guided setup: one question group per screen over the builder's own state, a live receipt beside it, and a
 * finish that hands the result to an AI agent or to a script. Every answer is a change the full builder shows.
 */
export function GuidedSetup(props: Props) {
  const { step, onStep, onOpenBuilder, issues } = props;
  const current = guideSteps[step];
  const heading = useRef<HTMLHeadingElement>(null);
  const [direction, setDirection] = useState<'forward' | 'back'>('forward');
  // A step change moves focus to its heading, so keyboard and screen reader users start at the top of the new step.
  const first = useRef(true);
  useEffect(() => {
    if (first.current) { first.current = false; return; }
    heading.current?.focus({ preventScroll: true });
    heading.current?.closest('.cb-wiz')?.scrollIntoView({ block: 'start', behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }, [step]);

  const blocking = issues.filter((issue) => issue.severity === 'blocking').length;
  const go = (to: number) => { setDirection(to >= step ? 'forward' : 'back'); onStep(Math.min(Math.max(to, 0), last)); };
  // Enter continues from anywhere on a step except text entry and buttons, which keep their own Enter.
  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || step === 0 || step === last) return;
    const target = event.target as HTMLElement;
    if (target.closest('button, a, select, textarea, summary') || (target instanceof HTMLInputElement && !['radio', 'checkbox'].includes(target.type))) return;
    event.preventDefault();
    go(step + 1);
  };

  return <div className="cb-wiz">
    <nav className="cb-wiz-steps" aria-label="Setup steps">
      <ol>{guideSteps.map((entry, index) => {
        if (index === 0) return null;
        const done = index < step;
        return <li key={entry.id}>
          <button type="button" className="cb-wiz-seg" aria-current={index === step ? 'step' : undefined} data-done={done || undefined}
            data-flag={entry.id === 'review' && blocking > 0 ? true : undefined} onClick={() => go(index)}>
            <span className="cb-wiz-seg-bar" aria-hidden="true" />
            <span className="cb-wiz-seg-name"><span className="cb-wiz-seg-num" aria-hidden="true">{done ? <Check /> : index}</span>{entry.short}
              {entry.id === 'review' && blocking > 0 && <span className="sr-only">: {plural(blocking, 'problem')} to fix</span>}</span>
          </button>
        </li>;
      })}</ol>
      <p className="cb-wiz-where" aria-hidden="true">{step === 0 ? 'Start' : `${step} of ${last} · ${current.short}`}</p>
    </nav>

    <div className="cb-wiz-body" data-step={current.id}>
      <section className="cb-wiz-stage" aria-labelledby="cb-guide-title" onKeyDown={onKey}>
        <div key={step} className="cb-wiz-panel" data-direction={direction}>
          <header className="cb-wiz-head">
            <h2 id="cb-guide-title" ref={heading} tabIndex={-1}>{current.title}</h2>
            <p className="cb-wiz-lead">{current.lead}</p>
          </header>
          <p className="sr-only" role="status" aria-live="polite">{step === 0 ? '' : `Step ${step} of ${last}: ${current.short}`}</p>
          {current.id === 'path' && <PathStep {...props} />}
          {current.id === 'environment' && <EnvironmentStep {...props} />}
          {current.id === 'product' && <ProductStep {...props} />}
          {(current.id === 'runtime' || current.id === 'security' || current.id === 'messages') && <DecisionStep {...props} keys={stepKeys(current)} />}
          {current.id === 'review' && <ReviewStep {...props} />}
          {current.id === 'export' && <FinishStep {...props} />}
        </div>

        {step > 0 && <footer className="cb-wiz-foot">
          <button type="button" className="cb-wiz-back" onClick={() => go(step - 1)}><ArrowLeft aria-hidden="true" />Back</button>
          {step < last
            ? <>
              <span className="cb-wiz-kbd" aria-hidden="true">Press <kbd>Enter</kbd> to continue</span>
              <button type="button" className="cb-wiz-next" onClick={() => go(step + 1)}>{step === last - 1 ? 'Finish' : `Next: ${guideSteps[step + 1].short}`}<ArrowRight aria-hidden="true" /></button>
            </>
            : <button type="button" className="cb-wiz-back cb-wiz-push" onClick={onOpenBuilder}><SlidersHorizontal aria-hidden="true" />Fine-tune in the full builder</button>}
        </footer>}
      </section>

      {step > 0 && step < last && <Receipt {...props} />}
    </div>
  </div>;
}

/** The live receipt: what the answers add up to so far. */
function Receipt({ state, outputs, issues, onStep }: Props) {
  const included = modules.filter((module) => module.id !== 'Platform' && isModuleIncluded(module, state.symbols));
  const blocking = issues.filter((issue) => issue.severity === 'blocking').length;
  const warnings = issues.filter((issue) => issue.severity === 'warning').length;
  const dotenv = outputs.environment.id === 'Docker' ? outputs.variables.dotenv : '';
  const changes = outputs.environmentFile.count + (dotenv ? outputs.environmentFile.viaDotenv.length : 0);
  const EnvIcon = environmentIcon[outputs.environment.id] ?? Server;
  const file = outputs.changesOnlyFile.text.trim() === '{}' ? '' : outputs.changesOnlyFile.text;
  return <aside className="cb-wiz-receipt" aria-label="Your configuration so far">
    <p className="cb-wiz-receipt-title">Your configuration</p>
    <dl className="cb-wiz-facts">
      <div><dt>Runs in</dt><dd><EnvIcon aria-hidden="true" />{outputs.environment.label}</dd></div>
      <div><dt>Changes</dt><dd className="cb-wiz-num">{changes}</dd></div>
      <div><dt>Secrets</dt><dd className="cb-wiz-num">{outputs.secrets.needed.length}</dd></div>
    </dl>
    <ul className="cb-wiz-chips" aria-label={`${included.length} modules`}>
      {included.map((module) => <li key={module.id} data-family={module.family} title={module.name}><span aria-hidden="true">{module.symbol}</span><span className="sr-only">{module.name}</span></li>)}
    </ul>
    <button type="button" className="cb-wiz-health" data-severity={blocking ? 'blocking' : warnings ? 'warning' : 'clear'} onClick={() => onStep(guideSteps.findIndex((entry) => entry.id === 'review'))}>
      {blocking ? <OctagonX aria-hidden="true" /> : warnings ? <AlertTriangle aria-hidden="true" /> : <CircleCheck aria-hidden="true" />}
      <span>{blocking ? `${plural(blocking, 'problem')} would stop startup` : warnings ? `Starts. ${plural(warnings, 'recommendation')}` : outputs.environment.relaxed ? 'Starts in Development' : 'Ready for production'}</span>
      <ChevronRight aria-hidden="true" />
    </button>
    <CodeView title={outputs.environmentFile.fileName} language="json" code={file} empty="Nothing differs from the shipped file yet. Your answers appear here as you go." />
    {dotenv && <CodeView title=".env" language="shell" code={dotenv} />}
  </aside>;
}

// ── Building blocks ──────────────────────────────────────────────────────────────────────────────────────────────

type Tile = Option & { icon?: LucideIcon };

/** Radio tiles: every option with its meaning, so the choice is made by reading, not by guessing a label. */
function Tiles({ name, legend, options, value, onChange, describedBy, columns }: { name: string; legend: ReactNode; options: Tile[]; value: string; onChange: (value: string) => void; describedBy?: string; columns?: number }) {
  return <fieldset className="cb-wiz-tiles" style={columns ? { '--cols': columns } as React.CSSProperties : undefined} aria-describedby={describedBy}>
    <legend className="sr-only">{legend}</legend>
    {options.map((option) => {
      const Icon = option.icon;
      return <label key={option.value} className="cb-wiz-tile">
        <input type="radio" name={name} value={option.value} checked={value === option.value} onChange={() => onChange(option.value)} />
        <span className="cb-wiz-tile-top">{Icon && <Icon aria-hidden="true" />}<span className="cb-wiz-tile-name">{option.label ?? option.value}</span><span className="cb-wiz-tick" aria-hidden="true"><Check /></span></span>
        {option.hint && <span className="cb-wiz-tile-hint">{inline(option.hint)}</span>}
      </label>;
    })}
  </fieldset>;
}

/** One setting as a question: its name and one line of help, the control, its checks and a way back to the default. */
function Question({ field, props }: { field: Field; props: Props }) {
  const { state, issuesByPath, secretNote, onChange, onReset } = props;
  const value = valueOf(field, state);
  const changed = isChanged(field, state);
  const id = fieldId(field.path);
  const help = `${id}-help`;
  const issues = issuesByPath.get(field.path) ?? [];
  const severity = issues.some((issue) => issue.severity === 'blocking') ? 'blocking' : issues.some((issue) => issue.severity === 'warning') ? 'warning' : undefined;
  const tiles = field.control === 'enum' && (field.options?.length ?? 0) > 1 && (field.options?.length ?? 0) <= 4;
  const label = labelOf(field);
  const toggle = field.control === 'boolean';
  const control = <FieldControl field={field} value={value} controlId={id} onChange={(next) => onChange(field, next)} describedBy={help} secretNote={field.secret ? secretNote(field) : undefined} />;
  return <div id={`${id}-row`} className="cb-q" data-changed={changed || undefined} data-issue={severity} data-toggle={toggle || undefined}>
    <div className="cb-q-head">
      {tiles || isGroupControl(field)
        ? <span id={`${id}-label`} className="cb-q-label">{label}</span>
        : <label id={`${id}-label`} htmlFor={id} className="cb-q-label">{label}</label>}
      <span className="cb-q-meta">
        {changed && !field.secret && <button type="button" className="cb-q-reset" onClick={() => onReset(field)} aria-label={`Reset ${label} to its default`}><RotateCcw aria-hidden="true" />Reset</button>}
        {field.guide && <Link href={field.guide} className="cb-q-docs" aria-label={`Docs for ${field.path}`}><BookOpen aria-hidden="true" />Docs</Link>}
      </span>
    </div>
    <p id={help} className="cb-q-help">{inline(helpOf(field))}</p>
    {tiles
      ? <Tiles name={id} legend={label} options={field.options ?? []} value={String(value ?? '')} onChange={(next) => onChange(field, next)} describedBy={help} />
      : toggle ? <div className="cb-q-toggle">{control}</div> : control}
    {issues.map((issue) => <p key={issue.id} className="cb-q-issue" data-severity={issue.severity}>{issue.severity === 'blocking' ? <OctagonX aria-hidden="true" /> : <AlertTriangle aria-hidden="true" />}{issue.title}</p>)}
  </div>;
}

function visibleFields(keys: string[], { byPath, state }: Props): Field[] {
  return keys.flatMap((key) => {
    const field = byPath.get(key);
    return field && isVisible(field, byPath, state) ? [field] : [];
  });
}

// ── Steps ────────────────────────────────────────────────────────────────────────────────────────────────────────

function PathStep({ path, onPath, onStep, onOpenBuilder }: Props) {
  return <>
    <ul className="cb-wiz-doors">{guidePaths.map((entry) => {
      const Icon = pathIcon[entry.id as GuidePath];
      return <li key={entry.id}>
        <button type="button" className="cb-wiz-door" aria-pressed={path === entry.id} onClick={() => { onPath(entry.id as GuidePath); onStep(1); }}>
          <span className="cb-wiz-door-icon" aria-hidden="true"><Icon /></span>
          <span className="cb-wiz-door-title">{entry.title}</span>
          <span className="cb-wiz-door-detail">{entry.detail}</span>
          <span className="cb-wiz-door-go" aria-hidden="true">Start<ArrowRight /></span>
        </button>
      </li>;
    })}</ul>
    <ul className="cb-wiz-promises">
      <li><CircleCheck aria-hidden="true" />About three minutes</li>
      <li><CircleCheck aria-hidden="true" />Nothing leaves your browser</li>
      <li><CircleCheck aria-hidden="true" />Secrets are never asked for</li>
    </ul>
    <p className="cb-wiz-aside">Know your settings already? <button type="button" className="cb-more" onClick={onOpenBuilder}>Open the full builder</button>: every key on one sheet.</p>
  </>;
}

function EnvironmentStep({ state, onEnvironment }: Props) {
  return <Tiles name="cb-guide-environment" legend="Where will it run?" columns={3} value={state.environment} onChange={onEnvironment}
    options={environments.map((entry) => ({ value: entry.id, label: entry.label, hint: entry.hint.replace(/(docker-compose\.yml|ASPNETCORE_ENVIRONMENT=\w+|appsettings\.json)/g, '`$1`'), icon: environmentIcon[entry.id] }))} />;
}

function ProductStep(props: Props) {
  const { catalog, state, productName, onProductName, onSymbol } = props;
  const runtime = visibleFields(stepKeys(guideSteps[2]), props);
  const choices = catalog.symbols.filter((symbol) => symbol.type === 'choice');
  return <div className="cb-wiz-stack">
    <div className="cb-q">
      <div className="cb-q-head"><label htmlFor="cb-guide-name" className="cb-q-label">Product name</label></div>
      <p id="cb-guide-name-help" className="cb-q-help">dotnet new names the solution and its projects after it.</p>
      <input id="cb-guide-name" className="cb-input cb-wiz-name" value={productName} spellCheck={false} autoComplete="off" aria-describedby="cb-guide-name-help"
        onChange={(event) => onProductName(event.target.value.replace(/[^\w.]/g, ''))} />
      <p className="cb-wiz-command"><code>{dotnetNewCommand(catalog, state.symbols, productName || 'MyProduct')}</code></p>
    </div>
    <div className="cb-q">
      <div className="cb-q-head"><span id="cb-guide-modules" className="cb-q-label">Modules</span></div>
      <p className="cb-q-help">Press a module to leave it out or bring it back. Locked ones are part of every product.</p>
      <ModulePicker state={state} onSymbol={onSymbol} />
    </div>
    {choices.map((symbol) => <div key={symbol.id} className="cb-q">
      <div className="cb-q-head"><span className="cb-q-label">{symbol.label}</span></div>
      <Tiles name={`cb-guide-symbol-${symbol.id}`} legend={symbol.label} columns={symbol.choices?.length} value={String(state.symbols[symbol.id])}
        onChange={(value) => onSymbol(symbol.id, value)} options={(symbol.choices ?? []).map((choice) => ({ value: choice.value, hint: choice.description }))} />
    </div>)}
    {runtime.length > 0 && <>
      <h3 className="cb-wiz-subhead">Switched on at startup</h3>
      {runtime.map((field) => <Question key={field.path} field={field} props={props} />)}
    </>}
  </div>;
}

function DecisionStep(props: Props & { keys: string[] }) {
  const fields = visibleFields(props.keys, props);
  if (!fields.length) return <p className="cb-wiz-empty"><CircleCheck aria-hidden="true" />Nothing to decide here for the modules you kept. The defaults apply.</p>;
  return <div className="cb-wiz-stack">{fields.map((field) => <Question key={field.path} field={field} props={props} />)}</div>;
}

function ReviewStep(props: Props) {
  const { issues, byPath, state, catalog, onStep, outputs } = props;
  const blocking = issues.filter((issue) => issue.severity === 'blocking');
  const warnings = issues.filter((issue) => issue.severity === 'warning');
  const stepOf = (key: string) => guideSteps.findIndex((entry) => stepKeys(entry).includes(key));
  const fixable = (issue: Issue) => issue.paths.map((key) => byPath.get(key)).filter((field): field is Field => Boolean(field) && !field!.secret && isVisible(field!, byPath, state)).slice(0, 3);
  const included = modules.filter((module) => module.id !== 'Platform' && isModuleIncluded(module, state.symbols));
  const answered = decisions.flatMap((group) => group.keys).flatMap((key) => {
    const field = byPath.get(key);
    return field && isVisible(field, byPath, state) && isChanged(field, state) ? [{ field, value: valueOf(field, state) }] : [];
  });
  const severity = blocking.length ? 'blocking' : warnings.length ? 'warning' : 'clear';
  return <div className="cb-wiz-stack">
    <div className="cb-wiz-verdict" data-severity={severity} role="status">
      <span className="cb-wiz-verdict-icon" aria-hidden="true">{blocking.length ? <OctagonX /> : warnings.length ? <AlertTriangle /> : <CircleCheck />}</span>
      <div>
        <p className="cb-wiz-verdict-title">{blocking.length ? `${plural(blocking.length, 'problem')} would stop the API from starting` : outputs.environment.relaxed ? 'It starts in Development' : 'Nothing blocks startup'}</p>
        <p>{blocking.length ? 'Fix it here, or finish anyway: the hand-over says what is still open.' : warnings.length ? `${plural(warnings.length, 'recommendation')} for production below. Each is optional.` : 'Every production rule passes.'}</p>
      </div>
    </div>

    {[...blocking, ...warnings].map((issue) => {
      const fields = fixable(issue);
      const inStep = issue.paths.map(stepOf).find((index) => index > 0);
      return <details key={issue.id} className="cb-wiz-issue" data-severity={issue.severity} open={issue.severity === 'blocking'}>
        <summary>
          {issue.severity === 'blocking' ? <OctagonX aria-hidden="true" /> : <AlertTriangle aria-hidden="true" />}
          <span><span className="sr-only">{issue.severity === 'blocking' ? 'Stops startup: ' : 'Recommended: '}</span>{issue.title}</span>
          <ChevronRight aria-hidden="true" className="cb-wiz-issue-chevron" />
        </summary>
        <div className="cb-wiz-issue-body">
          <p className="cb-q-help">{issue.detail}{issue.guide && <> <Link href={issue.guide} className="cb-q-docs">Read why</Link></>}</p>
          {fields.map((field) => <Question key={field.path} field={field} props={props} />)}
          {!fields.length && inStep !== undefined && <button type="button" className="cb-more" onClick={() => onStep(inStep)}>Change it in {guideSteps[inStep].short}</button>}
        </div>
      </details>;
    })}

    <section className="cb-wiz-answers" aria-labelledby="cb-guide-answers">
      <h3 id="cb-guide-answers" className="cb-wiz-subhead">Your answers</h3>
      <ul>
        <Answer label="Runs in" value={outputs.environment.label} onEdit={() => onStep(1)} />
        <Answer label="Modules" value={`${included.length}: ${included.map((module) => module.name).join(', ')}`} onEdit={() => onStep(2)} />
        {catalog.symbols.filter((symbol) => symbol.type === 'choice').map((symbol) => <Answer key={symbol.id} label={symbol.label} value={String(state.symbols[symbol.id])} onEdit={() => onStep(2)} />)}
        {answered.map(({ field, value }) => <Answer key={field.path} label={labelOf(field)} value={Array.isArray(value) ? value.join(', ') : String(value)} onEdit={() => onStep(stepOf(field.path))} />)}
      </ul>
    </section>
  </div>;
}

function Answer({ label, value, onEdit }: { label: string; value: string; onEdit: () => void }) {
  return <li className="cb-wiz-answer"><span className="cb-wiz-answer-label">{label}</span><span className="cb-wiz-answer-value">{value}</span>
    <button type="button" className="cb-more" onClick={onEdit} aria-label={`Edit ${label}`}>Edit</button></li>;
}

function useDocsRoot() {
  const [root, setRoot] = useState(basePath);
  // eslint-disable-next-line react-hooks/set-state-in-effect -- the origin exists only in the browser
  useEffect(() => setRoot(`${window.location.origin}${basePath}`), []);
  return root;
}

function useCopy() {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  const timer = useRef<number>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);
  const copy = async (text: string) => {
    try { await navigator.clipboard.writeText(text); setStatus('copied'); } catch { setStatus('failed'); }
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setStatus('idle'), 2400);
  };
  return [status, copy] as const;
}

const scriptName = 'apply-starterkit-config.sh';

/** The finish: a summary, then the two ways to apply it, the reader's pick first. */
function FinishStep(props: Props) {
  const { catalog, state, outputs, issues, productName, path, onStep } = props;
  const docs = useDocsRoot();
  const name = productName || 'MyProduct';
  const command = dotnetNewCommand(catalog, state.symbols, name);
  const parts = useMemo(() => handover({ catalog, outputs, issues }), [catalog, outputs, issues]);
  const prompt = useMemo(() => configPrompt({ docs, productName: name, command, environment: outputs.environment, ...parts }), [docs, name, command, outputs.environment, parts]);
  const script = useMemo(() => applyScript(parts, outputs.environment), [parts, outputs.environment]);
  const [promptStatus, copyPrompt] = useCopy();
  const [scriptStatus, copyScript] = useCopy();
  const [commandStatus, copyCommand] = useCopy();
  const blocking = parts.blocking.length;
  const included = modules.filter((module) => module.id !== 'Platform' && isModuleIncluded(module, state.symbols)).length;
  const changes = outputs.environmentFile.count + (outputs.environment.id === 'Docker' ? outputs.environmentFile.viaDotenv.length : 0);
  const order: GuidePath[] = path === 'manual' ? ['manual', 'agent'] : ['agent', 'manual'];

  const cards: Record<GuidePath, ReactNode> = {
    agent: <article key="agent" className="cb-wiz-end" data-chosen={path === 'agent' || undefined} aria-labelledby="cb-end-agent">
      <span className="cb-wiz-end-icon" aria-hidden="true"><Bot /></span>
      <h3 id="cb-end-agent">Send it to your AI agent</h3>
      <p className="cb-wiz-end-lead">One prompt with your modules, the files and the secrets to create. The agent writes everything and checks the build.</p>
      <ol className="cb-wiz-end-steps"><li><span>Copy the prompt</span></li><li><span>Paste it into Claude Code, Cursor or Copilot, opened in the product folder</span></li></ol>
      <div className="cb-wiz-end-actions">
        <button type="button" className="cb-wiz-next" data-status={promptStatus} onClick={() => copyPrompt(prompt)}>
          {promptStatus === 'copied' ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}{promptStatus === 'copied' ? 'Prompt copied' : promptStatus === 'failed' ? 'Copy failed: use the preview' : 'Copy the prompt'}
        </button>
        <button type="button" className="cb-wiz-back" onClick={() => download('starterkit-config-prompt.md', prompt)}><Download aria-hidden="true" />Download</button>
      </div>
      <span className="sr-only" role="status">{promptStatus === 'copied' ? 'Prompt copied. Paste it into your AI agent.' : promptStatus === 'failed' ? 'The prompt could not be copied.' : ''}</span>
      <details className="cb-wiz-peek">
        <summary><ChevronRight aria-hidden="true" />Preview the prompt <span className="cb-wiz-num">{prompt.split('\n').length} lines</span></summary>
        <pre tabIndex={0}>{prompt}</pre>
      </details>
    </article>,
    manual: <article key="manual" className="cb-wiz-end" data-chosen={path === 'manual' || undefined} aria-labelledby="cb-end-manual">
      <span className="cb-wiz-end-icon" aria-hidden="true"><Terminal /></span>
      <h3 id="cb-end-manual">Apply it yourself</h3>
      <p className="cb-wiz-end-lead">One script writes {parts.files.length ? parts.files.map((file) => file.path.split('/').pop()).join(' and ') : 'your settings'}{parts.additions.length ? ', merges .env' : ''}{parts.secrets.script ? ' and creates the secrets' : ''}. Existing files are kept as .bak.</p>
      <ol className="cb-wiz-end-steps"><li><span>Download the script</span></li><li><span>Run <code>bash {scriptName}</code> in the product folder</span></li></ol>
      <div className="cb-wiz-end-actions">
        <button type="button" className="cb-wiz-next" onClick={() => download(scriptName, script)}><Download aria-hidden="true" />Download the script</button>
        <button type="button" className="cb-wiz-back" data-status={scriptStatus} onClick={() => copyScript(script)}>{scriptStatus === 'copied' ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}{scriptStatus === 'copied' ? 'Copied' : 'Copy'}</button>
      </div>
      <span className="sr-only" role="status">{scriptStatus === 'copied' ? 'Script copied.' : ''}</span>
      <details className="cb-wiz-peek">
        <summary><ChevronRight aria-hidden="true" />Preview the script <span className="cb-wiz-num">{script.split('\n').length} lines</span></summary>
        <pre tabIndex={0}>{script}</pre>
      </details>
      <details className="cb-wiz-peek">
        <summary><ChevronRight aria-hidden="true" />Or file by file</summary>
        <div className="cb-wiz-files">
          {parts.files.map((file) => <CodeView key={file.path} title={file.path} fileName={file.path.split('/').pop()} language="json" code={file.text} />)}
          {parts.additions.map((addition) => <CodeView key={addition.title} title={addition.language === 'yaml' ? 'docker-compose.config.yml' : '.env (add or replace)'} fileName={addition.language === 'yaml' ? 'docker-compose.config.yml' : 'env.additions'} language={addition.language as 'shell' | 'yaml'} code={addition.text} />)}
          {parts.secrets.list.length > 0 && <ul className="cb-secret-list">{parts.secrets.list.map((line: string) => <li key={line}><code>{line.split(': ')[0]}</code><span>{line.slice(line.indexOf(': ') + 2)}</span></li>)}</ul>}
        </div>
      </details>
    </article>,
  };

  return <div className="cb-wiz-stack">
    <div className="cb-wiz-ready">
      <span className="cb-wiz-ready-icon" aria-hidden="true">{blocking ? <AlertTriangle /> : <Check />}</span>
      <div>
        <p className="cb-wiz-ready-title">{blocking ? 'Ready, with one thing open' : 'Your configuration is ready'}</p>
        <ul className="cb-wiz-ready-facts">
          <li>{outputs.environment.label}</li>
          <li><span className="cb-wiz-num">{included}</span> modules</li>
          <li><span className="cb-wiz-num">{changes}</span> {changes === 1 ? 'change' : 'changes'}</li>
          <li><span className="cb-wiz-num">{outputs.secrets.needed.length}</span> secrets, by name only</li>
        </ul>
        {blocking > 0 && <p className="cb-wiz-ready-open">{parts.blocking.join('; ')} still stops startup. <button type="button" className="cb-more" onClick={() => onStep(guideSteps.findIndex((entry) => entry.id === 'review'))}>Fix it in Review</button></p>}
      </div>
    </div>

    <div className="cb-wiz-ends">{order.map((id) => cards[id])}</div>

    <p className="cb-wiz-generate">No product yet? Generate it first: <code>{command}</code>
      <button type="button" className="cb-more" onClick={() => copyCommand(`dotnet new install StarterKit.Templates\n${command}`)}>{commandStatus === 'copied' ? 'Copied' : 'Copy the commands'}</button></p>
  </div>;
}
