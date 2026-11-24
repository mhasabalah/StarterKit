'use client';
import { useId } from 'react';
import { Lock } from 'lucide-react';
import { modules, environments } from '@/lib/config-builder/overlay.mjs';
import { isModuleIncluded, dotnetNewCommand } from '@/lib/config-builder/model.mjs';
import { CopyButton } from './code-view';
import type { Catalog, State } from './types';

type Props = {
  catalog: Catalog;
  state: State;
  productName: string;
  onProductName: (name: string) => void;
  onSymbol: (symbol: string, value: string | boolean) => void;
  onEnvironment: (environment: string) => void;
};

/** The symbol a module tile switches, when it has one of its own. */
const toggleOf: Record<string, string> = { Catalog: 'sample', Search: 'search', Agentic: 'agentic' };

/** File names and variables in a hint print as code and may break only after `=`, `.` or `-`. */
function codeWords(text: string) {
  return text.split(/([\w-]+\.(?:ya?ml|json)|[A-Z][A-Z0-9_]+=\w+|appsettings\.json)/g).map((part, index) => (index % 2
    ? <code key={index}>{part.split(/(?<=[=.-])/).flatMap((piece, at) => (at ? [<wbr key={at} />, piece] : [piece]))}</code>
    : part));
}

/** Soft hyphens where a long module name may break inside a narrow cell. */
const cellName: Record<string, string> = { Subscriptions: 'Subscrip­tions', DataTransfer: 'Data­Transfer', Notification: 'Notifi­cation' };

type EnvironmentProps = { state: State; onEnvironment: (environment: string) => void; cards?: boolean; name?: string };

/** Where the API runs: a stacked segmented control in the builder, large cards with every hint in the guided setup. */
export function EnvironmentChoice({ state, onEnvironment, cards, name = 'cb-environment' }: EnvironmentProps) {
  const environment = environments.find((entry) => entry.id === state.environment);
  if (cards) {
    return <div className="cb-choice-cards" role="radiogroup" aria-label="Target environment">
      {environments.map((entry) => <label key={entry.id} className="cb-choice-card">
        <input type="radio" name={name} value={entry.id} checked={state.environment === entry.id} onChange={() => onEnvironment(entry.id)} />
        <span className="cb-choice-title">{entry.label}</span>
        <span className="cb-choice-detail">{codeWords(entry.hint)}</span>
      </label>)}
    </div>;
  }
  return <>
    <div className="cb-segmented cb-segmented-stack" role="radiogroup" aria-label="Target environment">
      {environments.map((entry) => <label key={entry.id} className="cb-segment">
        <input type="radio" name={name} value={entry.id} checked={state.environment === entry.id} onChange={() => onEnvironment(entry.id)} />
        <span>{entry.label}</span>
      </label>)}
    </div>
    {environment && <p className="cb-reading">{codeWords(environment.hint)}</p>}
  </>;
}

/**
 * The module picker prints the product's own chart: included modules are tinted cells, left-out ones become dashed
 * empty slots.
 */
export function ModulePicker({ state, onSymbol }: { state: State; onSymbol: (symbol: string, value: string | boolean) => void }) {
  return <>
    <ul className="cb-chart" aria-label="Modules in this product">
      {modules.filter((module) => module.id !== 'Platform').map((module) => {
        const included = isModuleIncluded(module, state.symbols);
        const symbol = toggleOf[module.id];
        const order = module.order === undefined ? '' : String(module.order).padStart(3, '0');
        const body = <>
          <span className="cb-cell-meta" aria-hidden="true"><span>{order}</span>{!symbol && module.id !== 'Shipping' && <Lock />}</span>
          <span className="cb-cell-symbol" aria-hidden="true">{module.symbol}</span>
          <span className="cb-cell-name">{cellName[module.id] ?? module.name}</span>
        </>;
        return <li key={module.id}>
          {symbol
            ? <button type="button" className="cb-cell" data-family={module.family} data-included={included || undefined} aria-pressed={included}
                aria-label={`${module.name}: ${included ? 'included' : 'left out'}. ${module.description}`} onClick={() => onSymbol(symbol, !state.symbols[symbol])}>{body}</button>
            : <span className="cb-cell" data-family={module.family} data-included={included || undefined} data-fixed
                title={module.id === 'Shipping' ? 'Included with the samples and Agentic' : 'Always included'}
                aria-label={`${module.name}: ${module.id === 'Shipping' ? `${included ? 'included' : 'left out'}, follows the samples and Agentic` : 'always included'}`}>{body}</span>}
        </li>;
      })}
    </ul>
    <p className="cb-reading cb-legend"><Lock aria-hidden="true" />Always part of the product. Catalog stands for both samples.</p>
  </>;
}

/** The template's choice symbols (multi-tenancy, transport, storage), each a segmented control with its meaning. */
export function TemplateChoices({ catalog, state, onSymbol, name = 'cb-symbol' }: { catalog: Catalog; state: State; onSymbol: (symbol: string, value: string | boolean) => void; name?: string }) {
  const choices = catalog.symbols.filter((symbol) => symbol.type === 'choice');
  return <>{choices.map((symbol) => {
    const description = symbol.choices?.find((choice) => choice.value === state.symbols[symbol.id])?.description;
    return <fieldset key={symbol.id} className="cb-block">
      <legend className="cb-block-title">{symbol.label}</legend>
      <div className="cb-segmented" role="radiogroup" aria-label={symbol.label}>
        {symbol.choices?.map((choice) => <label key={choice.value} className="cb-segment">
          <input type="radio" name={`${name}-${symbol.id}`} value={choice.value} checked={state.symbols[symbol.id] === choice.value} onChange={() => onSymbol(symbol.id, choice.value)} />
          <span>{choice.value}</span>
        </label>)}
      </div>
      {description && <p className="cb-reading">{description}</p>}
    </fieldset>;
  })}</>;
}

/** The project name and the dotnet new command that generates a product with these modules. */
export function GenerateCommand({ catalog, state, productName, onProductName }: Pick<Props, 'catalog' | 'state' | 'productName' | 'onProductName'>) {
  const nameId = useId();
  return <div className="cb-block">
    <label className="cb-block-title" htmlFor={nameId}>Generate this product</label>
    <input id={nameId} className="cb-input" value={productName} spellCheck={false} autoComplete="off" onChange={(event) => onProductName(event.target.value.replace(/[^w.]/g, ''))} aria-describedby={`${nameId}-hint`} />
    <p id={`${nameId}-hint`} className="cb-reading">The project name. The command picks the same modules and presets.</p>
    <pre className="cb-command"><code>{dotnetNewCommand(catalog, state.symbols, productName || 'MyProduct')}</code></pre>
    <CopyButton text={() => dotnetNewCommand(catalog, state.symbols, productName || 'MyProduct')} label="Copy the dotnet new command">Copy command</CopyButton>
  </div>;
}

/** The builder's left rail: where the configuration runs and what the product contains. */
export function ProductPanel({ catalog, state, productName, onProductName, onSymbol, onEnvironment }: Props) {
  return <div className="cb-product">
    <fieldset className="cb-block">
      <legend className="cb-block-title">Where it runs</legend>
      <EnvironmentChoice state={state} onEnvironment={onEnvironment} />
    </fieldset>
    <fieldset className="cb-block">
      <legend className="cb-block-title">Modules</legend>
      <p className="cb-reading">Leave out a sample or an optional module and its settings leave with it.</p>
      <ModulePicker state={state} onSymbol={onSymbol} />
    </fieldset>
    <TemplateChoices catalog={catalog} state={state} onSymbol={onSymbol} />
    <GenerateCommand catalog={catalog} state={state} productName={productName} onProductName={onProductName} />
  </div>;
}
