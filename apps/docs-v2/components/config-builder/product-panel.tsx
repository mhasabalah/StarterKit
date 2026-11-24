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

/** Soft hyphens where a long module name may break inside a narrow cell. */
const cellName: Record<string, string> = { Subscriptions: 'Subscrip­tions', DataTransfer: 'Data­Transfer', Notification: 'Notifi­cation' };

/**
 * Step one: where the configuration runs and what the product contains. The module picker prints the product's own
 * chart: included modules are tinted cells, left-out ones become dashed empty slots.
 */
export function ProductPanel({ catalog, state, productName, onProductName, onSymbol, onEnvironment }: Props) {
  const environment = environments.find((entry) => entry.id === state.environment);
  const nameId = useId();
  const choices = catalog.symbols.filter((symbol) => symbol.type === 'choice');
  return <div className="cb-product">
    <fieldset className="cb-block">
      <legend className="cb-block-title">Where it runs</legend>
      <div className="cb-segmented cb-segmented-stack" role="radiogroup" aria-label="Target environment">
        {environments.map((entry) => <label key={entry.id} className="cb-segment">
          <input type="radio" name="cb-environment" value={entry.id} checked={state.environment === entry.id} onChange={() => onEnvironment(entry.id)} />
          <span>{entry.label}</span>
        </label>)}
      </div>
      {environment && <p className="cb-reading">{environment.hint}</p>}
    </fieldset>

    <fieldset className="cb-block">
      <legend className="cb-block-title">Modules</legend>
      <p className="cb-reading">Leave out a sample or an optional module and its settings leave with it.</p>
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
    </fieldset>

    {choices.map((symbol) => <fieldset key={symbol.id} className="cb-block">
      <legend className="cb-block-title">{symbol.label}</legend>
      <div className="cb-segmented" role="radiogroup" aria-label={symbol.label}>
        {symbol.choices?.map((choice) => <label key={choice.value} className="cb-segment">
          <input type="radio" name={`cb-symbol-${symbol.id}`} value={choice.value} checked={state.symbols[symbol.id] === choice.value} onChange={() => onSymbol(symbol.id, choice.value)} />
          <span>{choice.value}</span>
        </label>)}
      </div>
      {symbol.choices?.find((choice) => choice.value === state.symbols[symbol.id])?.description && <p className="cb-reading">{symbol.choices.find((choice) => choice.value === state.symbols[symbol.id])?.description}</p>}
    </fieldset>)}

    <div className="cb-block">
      <label className="cb-block-title" htmlFor={nameId}>Generate this product</label>
      <input id={nameId} className="cb-input" value={productName} spellCheck={false} autoComplete="off" onChange={(event) => onProductName(event.target.value.replace(/[^\w.]/g, ''))} aria-describedby={`${nameId}-hint`} />
      <p id={`${nameId}-hint`} className="cb-reading">The project name. The command picks the same modules and presets.</p>
      <pre className="cb-command"><code>{dotnetNewCommand(catalog, state.symbols, productName || 'MyProduct')}</code></pre>
      <CopyButton text={() => dotnetNewCommand(catalog, state.symbols, productName || 'MyProduct')} label="Copy the dotnet new command">Copy command</CopyButton>
    </div>
  </div>;
}
