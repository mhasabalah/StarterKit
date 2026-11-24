// Live checks of the configuration builder. `blocking` rules mirror what makes the API refuse to start
// (src/Api/Infrastructure/ProductionConfigurationGuard.cs and the options validators); `warning` rules are the
// production recommendations of CLAUDE.md and the wiki. tests/config-builder-model.test.mjs covers each one.
import { PLACEHOLDER, isVisible, lookup, valueOf, secretsNeeded } from './model.mjs';

/**
 * @typedef {'blocking' | 'warning' | 'info'} Severity
 * @typedef {{ id: string, severity: Severity, title: string, detail: string, paths: string[], guide?: string }} Issue
 */

const text = (value) => (value === null || value === undefined ? '' : String(value));
const isTrue = (value) => value === true || /^true$/i.test(text(value));
const lower = (value) => text(value).toLowerCase();

/**
 * @param {ReturnType<typeof import('./model.mjs').createContext>} context
 * @param {import('./model.mjs').Field[]} fields
 * @param {import('./model.mjs').State} state
 * @returns {Issue[]}
 */
export function evaluateRules(context, fields, state) {
  const byPath = lookup(fields);
  const get = (path) => { const field = byPath.get(path); return field ? valueOf(field, state) : undefined; };
  const has = (path) => byPath.has(path);
  const environment = context.environment.id;
  const relaxed = context.environment.relaxed;
  const production = environment === 'Production';
  /** @type {Issue[]} */
  const issues = [];
  const add = (issue) => issues.push(issue);

  if (!relaxed) {
    // EnsureNoPlaceholders: every key, shown or not. Secrets are supplied from the Secrets list, so only the others.
    const placeholders = fields.filter((field) => !field.secret && text(valueOf(field, state)).toUpperCase().includes(PLACEHOLDER));
    if (placeholders.length) add({
      id: 'placeholders', severity: 'blocking', title: `${placeholders.length} value${placeholders.length === 1 ? '' : 's'} still CHANGE_ME`,
      detail: `Outside Development the API refuses to start while any value contains ${PLACEHOLDER}.`,
      paths: placeholders.map((field) => field.path), guide: '/deployment/secrets/',
    });

    // EnsureDurableMessaging
    const transport = lower(get('Messaging:Transport'));
    if (!transport || transport === 'inmemory') add({
      id: 'transport-inmemory', severity: 'blocking', title: 'In-memory messaging outside Development',
      detail: 'InMemory is not durable: integration events and outbox deliveries in flight are lost on every restart, so the API refuses to start. Choose PostgreSql or RabbitMq.',
      paths: ['Messaging:Transport'], guide: '/architecture/messaging/',
    });
    if (transport === 'rabbitmq') {
      const host = lower(get('Messaging:RabbitMq:Host'));
      if (!host || host === 'localhost') add({
        id: 'rabbitmq-host', severity: 'blocking', title: 'RabbitMQ has no broker address',
        detail: 'With Transport=RabbitMq the host must be set (localhost would dial the API itself). In Docker, add docker-compose.rabbitmq.yml.',
        paths: ['Messaging:RabbitMq:Host'], guide: '/architecture/messaging/',
      });
      if (environment === 'Docker') add({
        id: 'rabbitmq-overlay', severity: 'info', title: 'Add the RabbitMQ overlay',
        detail: 'Run Compose with -f docker-compose.rabbitmq.yml: it starts the broker and mounts the rabbitmq_password secret.',
        paths: ['Messaging:Transport'], guide: '/deployment/docker-compose/',
      });
    }

    // EnsurePaymentGatewayModes (the Production-only part; key/mode mismatches need the real keys)
    for (const field of fields.filter((entry) => /^Subscriptions:PaymentGateways:[^:]+:Enabled$/.test(entry.path))) {
      if (!isTrue(valueOf(field, state))) continue;
      const gateway = field.path.split(':')[2];
      const live = lower(get(`Subscriptions:PaymentGateways:${gateway}:Mode`)) === 'live';
      if (production && !live && !isTrue(get(`Subscriptions:PaymentGateways:${gateway}:AllowSandboxInProduction`))) add({
        id: `gateway-sandbox-${gateway}`, severity: 'blocking', title: `${gateway} is a sandbox gateway in Production`,
        detail: 'Production refuses an enabled Sandbox gateway. Use live keys with Mode=Live, or set AllowSandboxInProduction for a demo stack.',
        paths: [`Subscriptions:PaymentGateways:${gateway}:Mode`], guide: '/subscriptions/online-payments/',
      });
      if (!production && !live) add({
        id: `gateway-sandbox-${gateway}`, severity: 'info', title: `${gateway} runs in Sandbox mode`,
        detail: 'Sandbox takes test keys only. The API also refuses a live key in Sandbox mode (and a test key in Live mode).',
        paths: [`Subscriptions:PaymentGateways:${gateway}:Mode`], guide: '/subscriptions/online-payments/',
      });
    }

    // Agentic: the Scripted provider is refused in Production (AgenticOptions validation).
    if (has('Agentic:Enabled') && isTrue(get('Agentic:Enabled'))) {
      const scripted = fields.filter((entry) => /^Agentic:Providers:[^:]+:Kind$/.test(entry.path) && lower(valueOf(entry, state)) === 'scripted');
      const used = scripted.filter((entry) => {
        const provider = entry.path.split(':')[2];
        return fields.some((model) => /^Agentic:Models:[^:]+:Provider$/.test(model.path) && valueOf(model, state) === provider);
      });
      if (used.length) add({
        id: 'agentic-scripted', severity: production ? 'blocking' : 'warning',
        title: production ? 'Scripted agent provider in Production' : 'Scripted agent provider',
        detail: `A model uses the Scripted provider, which answers from test scripts. It is refused when ASPNETCORE_ENVIRONMENT=Production. Add an OpenAiCompatible or Anthropic provider and point the models at it.`,
        paths: used.map((entry) => entry.path), guide: '/agentic/configuration/',
      });
    }

    // Recommendations
    if (isTrue(get('Database:MigrateOnStartup'))) add({
      id: 'migrate-on-startup', severity: 'warning', title: 'Migrations run at boot',
      detail: 'In production, migrations run once at deploy with --migrate-only (under an advisory lock), not in every replica at startup.',
      paths: ['Database:MigrateOnStartup'], guide: '/architecture/databaseconventions/',
    });
    if (!isTrue(get('Attachment:Validation:RequireVirusScanning'))) add({
      id: 'virus-scanning', severity: 'warning', title: 'Uploads are not required to be scanned',
      detail: 'Production should set RequireVirusScanning=true with the ClamAV scanner (docker-compose.clamav.yml). Scanning fails closed.',
      paths: ['Attachment:Validation:RequireVirusScanning'], guide: '/attachment/security/',
    });
    if (has('MultiTenancy:Enabled') && isTrue(get('MultiTenancy:Enabled')) && text(get('MultiTenancy:Database:RlsBypass')) !== 'Role') add({
      id: 'rls-bypass', severity: 'warning', title: 'Row-level security can be lifted by the runtime role',
      detail: 'Production should run RlsBypass=Role: host-scope work connects as a BYPASSRLS role and the runtime role cannot switch RLS off.',
      paths: ['MultiTenancy:Database:RlsBypass'], guide: '/multitenancy/isolation-and-dedicated-databases/',
    });
    if (has('Hangfire:DashboardAuth:Enabled') && !isTrue(get('Hangfire:DashboardAuth:Enabled'))) add({
      id: 'hangfire-auth', severity: 'warning', title: 'The Hangfire dashboard has no authentication',
      detail: 'Anyone who reaches the dashboard path can see and trigger jobs.',
      paths: ['Hangfire:DashboardAuth:Enabled'], guide: '/deployment/',
    });
    const origins = get('Cors:AllowedOrigins');
    if (Array.isArray(origins) && origins.some((origin) => /localhost|127\.0\.0\.1/.test(text(origin)))) add({
      id: 'cors-localhost', severity: 'warning', title: 'CORS allows localhost',
      detail: 'Remove local development origins from production. Note that an override cannot shorten the base list: use the full export.',
      paths: ['Cors:AllowedOrigins'], guide: '/architecture/apiguidelines/',
    });
    const examples = fields.filter((field) => !field.secret && isVisible(field, byPath, state) && /(^|[./@])example\.com\b/i.test(text(valueOf(field, state))) && /Url|Host|Email|Address|Domain|Origins/i.test(field.path));
    if (examples.length) add({
      id: 'example-hosts', severity: 'warning', title: `${examples.length} value${examples.length === 1 ? '' : 's'} still on example.com`,
      detail: 'Links in emails, SSO return addresses and passkey origins must use your real domains.',
      paths: examples.map((field) => field.path),
    });
    if (isTrue(get('ForwardedHeaders:Enabled')) && !isTrue(get('ForwardedHeaders:TrustPrivateNetworks'))) {
      const proxies = get('ForwardedHeaders:KnownProxies');
      const networks = get('ForwardedHeaders:KnownNetworks');
      if ((!Array.isArray(proxies) || !proxies.length) && (!Array.isArray(networks) || !networks.length)) add({
        id: 'forwarded-headers', severity: 'warning', title: 'No trusted proxy',
        detail: 'Behind a reverse proxy the client IP (rate limits, audit) is the proxy\'s unless its address or network is trusted.',
        paths: ['ForwardedHeaders:KnownProxies', 'ForwardedHeaders:KnownNetworks'], guide: '/architecture/request-lifecycle/',
      });
    }
  }

  // Valid everywhere
  if (isTrue(get('Attachment:Validation:RequireVirusScanning')) && lower(get('Attachment:Validation:Scanner:Provider')) === 'none') add({
    id: 'scanner-missing', severity: relaxed ? 'warning' : 'blocking', title: 'Scanning is required but no scanner is set',
    detail: 'Uploads fail closed with 503 malware_scanner_unavailable. Choose the ClamAv scanner.',
    paths: ['Attachment:Validation:Scanner:Provider'], guide: '/attachment/security/',
  });
  if (has('Search:Engine') && lower(get('Search:Engine')) === 'meilisearch' && !text(get('Search:Meilisearch:Url'))) add({
    id: 'meilisearch-url', severity: 'blocking', title: 'Meilisearch has no URL',
    detail: 'Engine=Meilisearch needs Search:Meilisearch:Url (and a scoped API key secret). In Docker, add docker-compose.meilisearch.yml.',
    paths: ['Search:Meilisearch:Url'], guide: '/search/meilisearch/',
  });
  if (isTrue(get('Realtime:Enabled')) && isTrue(get('Realtime:UseRedisBackplane')) && !isTrue(get('Redis:Enabled'))) add({
    id: 'realtime-backplane', severity: 'warning', title: 'The real-time backplane needs Redis',
    detail: 'With several replicas, hub events reach only the sockets on the replica that sent them.',
    paths: ['Realtime:UseRedisBackplane', 'Redis:Enabled'], guide: '/modules/notification-realtime-and-push/',
  });
  const order = get('Notification:DefaultChannelOrder');
  if (Array.isArray(order) && order.includes('WhatsApp') && !text(get('WhatsApp:PhoneNumberId')) && !relaxed) add({
    id: 'whatsapp-unconfigured', severity: 'info', title: 'WhatsApp is first in the chain but not configured',
    detail: 'Without a phone number id every WhatsApp attempt fails over to the next channel.',
    paths: ['Notification:DefaultChannelOrder', 'WhatsApp:PhoneNumberId'], guide: '/modules/notification/',
  });
  if (!relaxed && environment === 'Docker') {
    const transport = text(get('Messaging:Transport'));
    if (transport && transport !== context.symbols.transport && transport !== 'InMemory') add({
      id: 'transport-template', severity: 'info', title: `Transport differs from the template choice`,
      detail: `The product was generated with --transport ${context.symbols.transport}; this sets ${transport} through MESSAGING_TRANSPORT in .env.`,
      paths: ['Messaging:Transport'],
    });
  }
  const secrets = secretsNeeded(context, fields, state).filter((field) => !field.optional);
  if (secrets.length && !relaxed) add({
    id: 'secrets', severity: 'info', title: `${secrets.length} secret${secrets.length === 1 ? '' : 's'} to supply`,
    detail: 'Secrets never go in an appsettings file. The Secrets tab lists each one and how it reaches the API.',
    paths: secrets.map((field) => field.path), guide: '/deployment/secrets/',
  });

  // Only issues about fields this product has.
  return issues.map((issue) => ({ ...issue, paths: issue.paths.filter((path) => byPath.has(path)) }))
    .filter((issue) => issue.paths.length || issue.id === 'secrets')
    .sort((a, b) => rank[a.severity] - rank[b.severity]);
}

const rank = { blocking: 0, warning: 1, info: 2 };
