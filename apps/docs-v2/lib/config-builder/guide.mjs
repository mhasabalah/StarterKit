/**
 * The guided setup: the same configuration as the full builder, asked one decision group at a time. Step 0 asks how
 * the result will be applied (an AI agent or by hand); the last step hands it over. The decision groups come from
 * `decisions` in overlay.mjs, so a step never names a key the builder does not have (tests/config-catalog.test.mjs).
 */
import { decisions } from './overlay.mjs';

/** @typedef {'agent' | 'manual'} GuidePath */
/**
 * @typedef {object} GuideStep
 * @property {string} id
 * @property {string} title       The step's heading.
 * @property {string} short       The name in the step rail.
 * @property {string} lead        One sentence under the heading: what this step decides.
 * @property {string[]} groups    Decision groups (overlay.mjs `decisions`) asked in this step.
 * @property {string[]} [omit]    Keys of those groups the step leaves out, because a template choice on it sets them.
 */

/** @type {GuideStep[]} */
export const guideSteps = [
  { id: 'path', title: 'How will you apply it?', short: 'How to apply', lead: 'Both ways ask the same questions. The last step gives you a prompt for your agent, or the files and the steps to follow.', groups: [] },
  { id: 'environment', title: 'Where will it run?', short: 'Where it runs', lead: 'This decides the file you get and which production rules the checks apply.', groups: [] },
  { id: 'product', title: 'What is in your product?', short: 'Your product', lead: 'The modules and template choices of dotnet new starterkit. Leave a module out and its settings leave with it.', groups: ['features'], omit: ['MultiTenancy:Enabled', 'MultiTenancy:DefaultIsolation'] },
  { id: 'runtime', title: 'What does it run on?', short: 'Infrastructure', lead: 'Messaging, cache, file storage and the database at startup.', groups: ['runtime'] },
  { id: 'security', title: 'How is it protected?', short: 'Security', lead: 'Two-factor for privileged roles, CAPTCHA, malware scanning and the jobs dashboard.', groups: ['security'] },
  { id: 'messages', title: 'How does it reach people?', short: 'Messages', lead: 'Email, the channel fallback order, SMS and push.', groups: ['messages'] },
  { id: 'review', title: 'Check it before you deploy', short: 'Review', lead: 'The same rules the API applies at startup. Fix what blocks it here; the rest are recommendations.', groups: [] },
  { id: 'export', title: 'Get your configuration', short: 'Get it', lead: 'Everything below is built from your answers. Secrets are listed by name and never included.', groups: [] },
];

/** The guide's paths, in the order the first step offers them. */
export const guidePaths = [
  { id: 'agent', title: 'Hand it to my AI agent', detail: 'Answer the questions, then copy one prompt. Your agent generates the product, writes the files and creates the secrets.' },
  { id: 'manual', title: 'Set it up by hand', detail: 'Answer the questions, then download one script that writes every file and creates the secrets. Or take the files one by one.' },
];

/** The keys a step asks, in the order of their decision groups. @param {GuideStep} step @returns {string[]} */
export function stepKeys(step) {
  return step.groups.flatMap((id) => decisions.find((group) => group.id === id)?.keys ?? []).filter((key) => !step.omit?.includes(key));
}

/** A step index from a hash or storage value, clamped to the steps that exist. @param {unknown} value */
export function clampStep(value) {
  const number = Number.parseInt(String(value ?? ''), 10);
  return Number.isFinite(number) ? Math.min(Math.max(number, 0), guideSteps.length - 1) : 0;
}

/** @param {unknown} value @returns {GuidePath | null} */
export const guidePathOf = (value) => (value === 'agent' || value === 'manual' ? value : null);

/**
 * Where one secret is supplied in an environment: user secrets, a Compose secret file, `.env`, or the host's secret
 * store. Shared by the builder's secret notes and the guide's hand-over, so both say the same thing.
 * @param {{ composeSecrets: Record<string, string> }} catalog @param {string} environment
 * @param {{ path: string, secretFile?: string, composeVariable?: string | null }} field
 */
export function secretNoteFor(catalog, environment, field) {
  if (environment === 'Development') return `Set it with dotnet user-secrets set "${field.path}".`;
  if (environment === 'Production') return `Set ${field.path.split(':').join('__')} from your secret store.`;
  if (field.composeVariable) return `Set ${field.composeVariable} in .env: Compose passes it to the API.`;
  const mountedBy = catalog.composeSecrets[field.secretFile ?? ''];
  const file = `secrets/${field.secretFile}.txt`;
  if (mountedBy === 'docker-compose.yml') return `Supplied by ${file}, mounted by docker-compose.yml.`;
  if (mountedBy) return `Supplied by ${file}, mounted by ${mountedBy} (add it with -f).`;
  return `Supplied by ${file}. Mount it with the snippet in Secrets.`;
}

/**
 * Everything the last step hands over, from the builder's exports: the files to write, the lines to add and the
 * secrets by name. It is the input of `configPrompt` (lib/agent-prompt.mjs) and the manual checklist alike.
 * @param {object} input
 * @param {{ composeSecrets: Record<string, string> }} input.catalog
 * @param {any} input.outputs           The builder's exports (components/config-builder/output-panel.tsx `Outputs`).
 * @param {{ severity: string, title: string }[]} input.issues
 */
export function handover({ catalog, outputs, issues }) {
  const { environment, environmentFile, variables, secrets } = outputs;
  const files = environment.id !== 'Production' || environmentFile.count > 0
    ? [{ path: `src/Api/${environmentFile.fileName}`, language: 'json', text: environmentFile.text }]
    : [];
  const additions = [];
  if (environment.id === 'Docker' && variables.dotenv.trim()) {
    additions.push({ title: 'Add these lines to .env next to docker-compose.yml (copy .env.example to .env first if it does not exist). Compose passes them to the API, and they win over appsettings files:', language: 'shell', text: variables.dotenv });
  }
  if (environment.id === 'Docker' && secrets.compose.trim()) {
    additions.push({ title: 'docker-compose.yml does not mount every secret this configuration needs. Create docker-compose.config.yml with this content, and start the stack with docker compose -f docker-compose.yml -f docker-compose.config.yml up -d from now on:', language: 'yaml', text: secrets.compose });
  }
  const list = secrets.needed.map((field) => `${field.path}: ${secretNoteFor(catalog, environment.id, field)}${field.optional ? ' Optional.' : ''}`);
  const script = environment.id === 'Development'
    ? secrets.script.replace(/GENERATE_A_RANDOM_VALUE/g, '$(openssl rand -base64 48)')
    : environment.id === 'Production' ? '' : secrets.script;
  return {
    files,
    additions,
    secrets: { list, script, language: 'shell' },
    blocking: issues.filter((issue) => issue.severity === 'blocking').map((issue) => issue.title),
    recommendations: issues.filter((issue) => issue.severity === 'warning').map((issue) => issue.title),
  };
}

const quote = (text) => `'${text.replace(/'/g, `'\''`)}'`;

/**
 * "Apply it yourself": one shell script, run from the product root, that writes the files, merges the .env lines and
 * creates the secrets. Existing files are kept as `.bak`; secret values are generated or left as PASTE_THE_VALUE.
 * @param {ReturnType<typeof handover>} parts
 * @param {{ id: string, label: string }} environment
 * @returns {string}
 */
export function applyScript(parts, environment) {
  const lines = [
    '#!/usr/bin/env bash',
    `# StarterKit configuration for ${environment.label} (ASPNETCORE_ENVIRONMENT=${environment.id}).`,
    '# Generated by the configuration builder. Run it from the product root: bash apply-starterkit-config.sh',
    'set -euo pipefail',
    '[ -f src/Api/appsettings.json ] || { echo "Run this from the root of a StarterKit product (src/Api/appsettings.json not found)." >&2; exit 1; }',
    '',
  ];
  for (const file of parts.files) {
    lines.push(`# ${file.path}`, `[ -f ${quote(file.path)} ] && cp ${quote(file.path)} ${quote(`${file.path}.bak`)}`, `cat > ${quote(file.path)} <<'STARTERKIT_EOF'`, file.text.trimEnd(), 'STARTERKIT_EOF', `echo "Wrote ${file.path}"`, '');
  }
  for (const addition of parts.additions) {
    if (addition.language === 'yaml') {
      lines.push('# Mount the secrets docker-compose.yml does not mount yet', `cat > docker-compose.config.yml <<'STARTERKIT_EOF'`, addition.text.trimEnd(), 'STARTERKIT_EOF',
        'echo "Wrote docker-compose.config.yml: start with docker compose -f docker-compose.yml -f docker-compose.config.yml up -d"', '');
      continue;
    }
    const entries = addition.text.split('\n').filter((line) => /^\w+=/.test(line));
    if (!entries.length) continue;
    const keys = entries.map((line) => line.split('=')[0]).join('|');
    lines.push('# .env: replace these keys, keep everything else', '[ -f .env ] || { [ -f .env.example ] && cp .env.example .env || touch .env; }', 'cp .env .env.bak',
      `grep -v -E '^(${keys})=' .env.bak > .env || true`, `cat >> .env <<'STARTERKIT_EOF'`, entries.join('\n'), 'STARTERKIT_EOF', `echo "Updated .env (${entries.length} key${entries.length === 1 ? '' : 's'})"`, '');
  }
  if (environment.id === 'Production') {
    if (parts.secrets.list.length) lines.push('# Secrets come from your platform\'s secret store. Set these environment variables there:', ...parts.secrets.list.map((line) => `echo ${quote(`  ${line.split(': ')[0].split(':').join('__')}`)}`), '');
  } else if (parts.secrets.script.trim()) {
    lines.push(environment.id === 'Docker' ? '# Secrets: one git-ignored file each. Random ones are generated; PASTE_THE_VALUE needs your provider\'s value.' : '# Secrets: dotnet user-secrets, outside the repository.', parts.secrets.script.trimEnd().replace('chmod 600 secrets/*','chmod 600 secrets/* 2>/dev/null || true'), '');
    if (environment.id === 'Docker') lines.push(`grep -l PASTE_THE_VALUE secrets/*.txt 2>/dev/null | sed 's/^/Fill in: /' || true`, '');
  }
  lines.push('echo "Done. Next: dotnet build, then dotnet test src/Api.Tests."');
  return `${lines.join('\n')}\n`;
}

/** One plain line per question of the guide, in the reader's terms (the reference text stays one click away). */
export const questionHelp = {
  'Messaging:Transport': 'How modules hand events to each other. Production needs one that survives a restart.',
  'Redis:Enabled': 'A shared cache and the real-time backplane. Turn it off only when one instance runs.',
  'Attachment:Storage:Provider': 'Where uploaded files are kept.',
  'Search:Engine': 'Where full-text search runs.',
  'Database:MigrateOnStartup': 'Apply migrations when the API starts. Production runs them once at deploy instead.',
  'Agentic:Enabled': 'The module is included either way. This turns agent runs on.',
  'Subscriptions:PaymentGateways:Stripe:Enabled': 'Take payments through Stripe. Its keys are secrets, set later.',
  'Subscriptions:PaymentGateways:Paymob:Enabled': 'Take payments through Paymob. Its keys are secrets, set later.',
  'Identity:TwoFactor:EnforcementMode': 'Whether admins and other privileged roles must sign in with two-factor.',
  'BotProtection:Enabled': 'A CAPTCHA on sign-up, sign-in, password reset and code resend.',
  'Attachment:Validation:RequireVirusScanning': 'Refuse to start unless every upload is scanned for malware.',
  'Attachment:Validation:Scanner:Provider': 'The scanner that checks each upload. ClamAV runs beside the API in Docker.',
  'Hangfire:DashboardAuth:Enabled': 'Ask for a sign-in before the background jobs dashboard opens.',
  'Email:Provider': 'How email leaves the API.',
  'Notification:DefaultChannelOrder': 'The order channels are tried in when one fails.',
  'SmsSettings:Provider': 'How SMS codes are sent.',
  'Push:Provider': 'How push notifications reach phones.',
};
