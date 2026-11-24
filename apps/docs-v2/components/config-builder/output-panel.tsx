'use client';
import Link from 'next/link';
import { useId, useState, type KeyboardEvent } from 'react';
import { AlertTriangle, ChevronDown, CircleCheck, Info, OctagonX } from 'lucide-react';
import { CodeView } from './code-view';
import type { Field, Issue, Warning } from './types';

export type Outputs = {
  environment: { id: string; label: string; relaxed: boolean };
  environmentFile: { fileName: string; text: string; count: number; viaDotenv: { field: Field; value: unknown }[]; warnings: Warning[] };
  changesOnlyFile: { text: string };
  fullFile: { fileName: string; text: string; count: number; warnings: Warning[] };
  variables: { dotenv: string; compose: string; count: number; warnings: Warning[] };
  secrets: { needed: Field[]; placeholderOnly: string[]; script: string; compose: string };
};

type Props = { outputs: Outputs; issues: Issue[]; onJump: (path: string) => void; secretNote: (field: Field) => string };

const tabs = [
  { id: 'environment', label: 'Environment file' },
  { id: 'full', label: 'Full file' },
  { id: 'variables', label: 'Variables' },
  { id: 'secrets', label: 'Secrets' },
] as const;
type Tab = (typeof tabs)[number]['id'];

const severityIcon = { blocking: OctagonX, warning: AlertTriangle, info: Info };
const severityLabel = { blocking: 'Stops the API from starting', warning: 'Recommended', info: 'Note' };

/** Checks first (what would stop the API), then the exports as tabs. */
export function OutputPanel({ outputs, issues, onJump, secretNote }: Props) {
  const [tab, setTab] = useState<Tab>('environment');
  const [onlyChanges, setOnlyChanges] = useState(false);
  // Checks open by themselves only while something stops startup; the reader can open or close them any time.
  const [checksOpen, setChecksOpen] = useState<boolean | null>(null);
  const base = useId();
  const blocking = issues.filter((issue) => issue.severity === 'blocking').length;
  const warnings = issues.filter((issue) => issue.severity === 'warning').length;
  const notes = issues.length - blocking - warnings;
  const open = checksOpen ?? blocking > 0;
  const onKey = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const next = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : -2;
    if (next === -2) return;
    event.preventDefault();
    const target = tabs[(next + tabs.length) % tabs.length].id;
    setTab(target);
    document.getElementById(`${base}-tab-${target}`)?.focus();
  };
  const { environmentFile: envFile, fullFile, variables, secrets, environment } = outputs;
  const docker = environment.id === 'Docker';
  const required = secrets.needed.filter((field) => !field.optional).length;
  return <div className="cb-output">
    <section className="cb-checks-panel" aria-labelledby={`${base}-checks`} data-blocking={blocking > 0 || undefined}>
      <h2 id={`${base}-checks`} className="cb-pane-title">
        <button type="button" className="cb-checks-toggle" aria-expanded={open} aria-controls={`${base}-checks-body`} onClick={() => setChecksOpen(!open)}>
          <span className="cb-checks-state" data-severity={blocking ? 'blocking' : warnings ? 'warning' : 'clear'} aria-hidden="true">{blocking ? <OctagonX /> : warnings ? <AlertTriangle /> : <CircleCheck />}</span>
          <span className="cb-checks-text">
            <span className="cb-checks-heading">Checks</span>
            <span className="cb-check-summary" role="status">{blocking ? `${blocking} block${blocking === 1 ? 's' : ''} startup` : 'Nothing blocks startup'}{warnings ? `, ${warnings} recommended` : ''}{notes ? `, ${notes} note${notes === 1 ? '' : 's'}` : ''}</span>
          </span>
          <ChevronDown aria-hidden="true" className="cb-section-chevron" />
        </button>
      </h2>
      {open && <div id={`${base}-checks-body`}>{issues.length === 0
        ? <p className="cb-all-clear"><CircleCheck aria-hidden="true" />{environment.relaxed ? 'Development tolerates placeholders and in-memory messaging. Switch to Docker or Production to check a deployment.' : 'No problems found for this environment.'}</p>
        : <ul className="cb-issues">{issues.map((issue) => {
          const Icon = severityIcon[issue.severity];
          return <li key={issue.id} className="cb-issue" data-severity={issue.severity}>
            <Icon aria-hidden="true" />
            <div>
              <p className="cb-issue-title"><span className="sr-only">{severityLabel[issue.severity]}: </span>{issue.title}</p>
              <p className="cb-issue-detail">{issue.detail}</p>
              <p className="cb-issue-actions">
                {issue.paths.slice(0, 4).map((path) => <button key={path} type="button" className="cb-key-button" onClick={() => onJump(path)}>{path}</button>)}
                {issue.paths.length > 4 && <span className="cb-muted">and {issue.paths.length - 4} more</span>}
                {issue.guide && <Link href={issue.guide} className="cb-doc-link">Read why</Link>}
              </p>
            </div>
          </li>;
        })}</ul>}
      </div>}
    </section>

    <section className="cb-export" aria-label="Export">
      <div className="cb-tabs" role="tablist" aria-label="Export format">
        {tabs.map((entry, index) => <button key={entry.id} id={`${base}-tab-${entry.id}`} type="button" role="tab" aria-selected={tab === entry.id} aria-controls={`${base}-panel`}
          tabIndex={tab === entry.id ? 0 : -1} className="cb-tab" onClick={() => setTab(entry.id)} onKeyDown={(event) => onKey(event, index)}>
          {entry.label}{entry.id === 'secrets' && required > 0 && <span className="cb-tab-count">{required}</span>}
        </button>)}
      </div>
      <div id={`${base}-panel`} role="tabpanel" aria-labelledby={`${base}-tab-${tab}`} className="cb-tab-panel" tabIndex={-1}>
        {tab === 'environment' && <>
          <p className="cb-reading">{envFile.fileName} sits next to appsettings.json and wins over it when ASPNETCORE_ENVIRONMENT={environment.id}.{environment.id !== 'Production' ? ' It starts from the file the template ships.' : ' The template ships none, so it holds only your changes.'}</p>
          {environment.id !== 'Production' && <label className="cb-check"><input type="checkbox" checked={onlyChanges} onChange={(event) => setOnlyChanges(event.target.checked)} /><span>Only my changes</span></label>}
          <Notes warnings={envFile.warnings} />
          {docker && envFile.viaDotenv.length > 0 && <div className="cb-note" data-severity="info">
            <p><strong>{envFile.viaDotenv.length} change{envFile.viaDotenv.length === 1 ? '' : 's'} go{envFile.viaDotenv.length === 1 ? 'es' : ''} to .env instead.</strong> Compose sets {envFile.viaDotenv.length === 1 ? 'this key' : 'these keys'} from environment variables, which win over any appsettings file. See Variables.</p>
          </div>}
          <CodeView title={envFile.fileName} fileName={envFile.fileName} language="json" code={onlyChanges && environment.id !== 'Production' ? outputs.changesOnlyFile.text : envFile.text} />
        </>}
        {tab === 'full' && <>
          <p className="cb-reading">The whole appsettings.json for the modules you picked, with every change applied. Use it for a new product, or to change or shorten what the base file lists.</p>
          <Notes warnings={fullFile.warnings} />
          <CodeView title="appsettings.json" fileName="appsettings.json" language="json" code={fullFile.text} />
        </>}
        {tab === 'variables' && <>
          <Notes warnings={variables.warnings} />
          {docker ? <>
            <p className="cb-reading">Compose reads <code>.env</code> next to docker-compose.yml and passes these values to the API.</p>
            <CodeView title=".env" fileName="env.additions" language="shell" code={variables.dotenv} empty="No change goes through .env." />
            <p className="cb-reading">Every other change as an extra Compose file. Start with <code>docker compose -f docker-compose.yml -f docker-compose.config.yml up -d</code>.</p>
            <CodeView title="docker-compose.config.yml" fileName="docker-compose.config.yml" language="yaml" code={variables.compose} empty="No other change." />
          </> : <>
            <p className="cb-reading">The same changes as environment variables (<code>Section__Key</code>), for {environment.id === 'Development' ? 'your shell or launchSettings.json' : 'Kubernetes, App Service or systemd'}. They win over every appsettings file.</p>
            <CodeView title="Environment variables" fileName="appsettings.env" language="shell" code={variables.dotenv} empty="No changes yet. Change a setting to see it here." />
          </>}
        </>}
        {tab === 'secrets' && <>
          <p className="cb-reading">{environment.id === 'Development'
            ? 'In Development, keep secrets in user secrets: they stay outside the repository.'
            : environment.id === 'Docker'
              ? 'Each secret is a file in secrets/ (git-ignored), mounted at /run/secrets. Nothing here goes into an appsettings file.'
              : 'Supply each secret from your platform\'s secret store as an environment variable.'}</p>
          {secrets.needed.length > 0 && <ul className="cb-secret-list">{secrets.needed.map((field) => <li key={field.path}>
            <button type="button" className="cb-key-button" onClick={() => onJump(field.path)}>{field.path}</button>
            <span>{secretNote(field)}{field.optional ? ' Optional.' : ''}{secrets.placeholderOnly.includes(field.path) ? ' Unused here, but appsettings.json ships CHANGE_ME for it, which stops startup: set it, even empty.' : ''}</span>
          </li>)}</ul>}
          <CodeView title={environment.id === 'Docker' ? 'Create the secret files' : environment.id === 'Development' ? 'User secrets' : 'Secret variables'} fileName={environment.id === 'Docker' ? 'create-secrets.sh' : undefined} language="shell" code={secrets.script} />
          {docker && secrets.compose && <>
            <p className="cb-reading">docker-compose.yml does not mount these yet. Add them to docker-compose.config.yml:</p>
            <CodeView title="Mount the extra secrets" language="yaml" code={secrets.compose} />
          </>}
          {docker && <p className="cb-reading"><Link href="/deployment/secrets/">Secrets guide</Link>: <code>./deployment/secrets-setup.sh</code> creates the random ones for you.</p>}
        </>}
      </div>
    </section>
  </div>;
}

function Notes({ warnings }: { warnings: Warning[] }) {
  if (!warnings.length) return null;
  return <div className="cb-note" data-severity="warning">{warnings.map((warning) => <p key={warning.path + warning.text}>{warning.text}</p>)}</div>;
}
