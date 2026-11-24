import Link from 'next/link';
import { ArrowRight, Bot, ListChecks } from 'lucide-react';
import generated from '@/.generated/config-catalog.json';
import { initialState, createContext, buildFields, withValue, buildEnvironmentFile, buildSecrets } from '@/lib/config-builder/model.mjs';
import { tokenize } from '@/lib/config-builder/format.mjs';
import { guideSteps, guidePaths } from '@/lib/config-builder/guide.mjs';
import type { Catalog, Field, State } from '@/components/config-builder/types';

const catalog = generated as unknown as Catalog;

/**
 * One example answer set, run through the builder's own model at build time, so the specimen on the home page is
 * exactly what the guided setup produces and cannot drift from the real appsettings files.
 */
const example = {
  environment: 'Docker',
  symbols: { transport: 'RabbitMq', storage: 'S3', agentic: false },
  values: [
    ['Identity:TwoFactor:EnforcementMode', 'Enforce'],
    ['Attachment:Validation:RequireVirusScanning', true],
    ['Attachment:Validation:Scanner:Provider', 'ClamAv'],
    ['Email:Provider', 'Smtp'],
  ] as [string, unknown][],
};

function specimen() {
  const start = initialState(catalog) as State;
  let state: State = { ...start, environment: example.environment, symbols: { ...start.symbols, ...example.symbols } };
  const context = createContext(catalog, state.symbols, state.environment);
  let fields = buildFields(context, state) as unknown as Field[];
  for (const [path, value] of example.values) {
    const field = fields.find((candidate) => candidate.path === path);
    if (!field) throw new Error(`Home specimen: ${path} is not a setting any more`);
    state = withValue(state, field as never, value) as State;
    fields = buildFields(context, state) as unknown as Field[];
  }
  const file = buildEnvironmentFile(context, fields as never, state, { onlyChanges: true });
  const secrets = buildSecrets(context, fields as never, state);
  return { fileName: file.fileName as string, text: file.text as string, changes: file.count as number, secrets: secrets.needed.length as number };
}

const sheet = specimen();
const tokens = tokenize(sheet.text, 'json') as [string, string][];
const doors = { agent: Bot, manual: ListChecks };
/** Steps 1 to 7: step 0 is the two doors beside them. */
const steps = guideSteps.slice(1);

export function HomeConfigure() {
  return <section className="home-configure" aria-labelledby="configure-title">
    <div className="configure-pitch">
      <h2 id="configure-title">Configure it before the first deploy</h2>
      <p className="configure-lead">Seven short steps turn the modules you keep into appsettings, environment variables and a secrets list, checked against the rules the API enforces at startup. Nothing leaves your browser.</p>
      <ul className="configure-doors">{guidePaths.map((path) => {
        const Icon = doors[path.id as keyof typeof doors];
        return <li key={path.id}>
          <Link href={`/tools/config-builder/#guided=${path.id}`} className="configure-door">
            <Icon aria-hidden="true" className="configure-door-icon" />
            <span className="configure-door-text"><b>{path.title}</b><span>{path.detail}</span></span>
            <ArrowRight aria-hidden="true" className="configure-door-arrow" />
          </Link>
        </li>;
      })}</ul>
      <p className="configure-more">Know your settings already? <Link href="/tools/config-builder/#builder">Open the full builder</Link>, every key on one sheet.</p>
    </div>

    <div className="configure-specimen">
      <ol className="configure-steps" aria-label="The steps of the guided setup">
        {steps.map((step, index) => <li key={step.id} data-last={index === steps.length - 1 || undefined}><span aria-hidden="true">{index + 1}</span>{step.short}</li>)}
      </ol>
      <figure className="configure-sheet">
        <figcaption><span>{sheet.fileName}</span><span>{sheet.changes} changes</span></figcaption>
        <pre tabIndex={0} aria-label={`Example ${sheet.fileName}`}><code>{tokens.map(([kind, text], index) => (kind === 'text' ? text : <span key={index} className={`cb-t-${kind}`}>{text}</span>))}</code></pre>
        <p className="configure-sheet-foot">An example: Docker, RabbitMQ, S3, enforced two-factor, ClamAV and SMTP. The {sheet.secrets} secrets it needs are listed by name, never stored.</p>
      </figure>
    </div>
  </section>;
}
