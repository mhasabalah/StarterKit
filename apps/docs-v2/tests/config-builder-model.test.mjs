// Behaviour of the configuration builder's model and checks: exports, secrets, layering, sharing and import.
import test from 'node:test';
import assert from 'node:assert/strict';
import { buildConfigCatalog } from '../scripts/config-catalog.mjs';
import {
  initialState, createContext, buildFields, lookup, withValue, addEntry, removeEntry, sanitizeState, buildEnvironmentFile,
  buildFullFile, buildEnvironmentVariables, buildSecrets, encodeState, decodeState, importSettings, dotnetNewCommand, isVisible,
} from '../lib/config-builder/model.mjs';
import { evaluateRules } from '../lib/config-builder/rules.mjs';
import { describeCron, describeTimespan, formatBytes, tokenize } from '../lib/config-builder/format.mjs';

const catalog = await buildConfigCatalog();

/** A builder session: state plus helpers that rebuild the context and fields like the page does. */
function session(environment = 'Docker', symbols = {}) {
  let state = { ...initialState(catalog), environment, symbols: { ...initialState(catalog).symbols, ...symbols } };
  const view = () => {
    const context = createContext(catalog, state.symbols, state.environment);
    const fields = buildFields(context, state);
    return { context, fields, byPath: lookup(fields) };
  };
  return {
    get state() { return state; },
    set: (path, value) => { const { byPath } = view(); const field = byPath.get(path); assert.ok(field, `no field ${path}`); state = withValue(state, field, value); },
    apply: (change) => { state = change(state); },
    view,
    rules: () => { const { context, fields } = view(); return evaluateRules(context, fields, state); },
  };
}
const ids = (issues, severity) => issues.filter((issue) => !severity || issue.severity === severity).map((issue) => issue.id);

test('the environment file holds the shipped file plus only what changed', () => {
  const s = session('Production');
  s.set('Messaging:Transport', 'PostgreSql');
  s.set('Paging:MaxPageSize', 50);
  const { context, fields } = s.view();
  const file = buildEnvironmentFile(context, fields, s.state);
  assert.equal(file.fileName, 'appsettings.Production.json');
  assert.deepEqual(JSON.parse(file.text), { Messaging: { Transport: 'PostgreSql' }, Paging: { MaxPageSize: 50 } });
  const docker = session('Docker');
  docker.set('Paging:MaxPageSize', 50);
  const view = docker.view();
  const shipped = JSON.parse(buildEnvironmentFile(view.context, view.fields, docker.state).text);
  assert.equal(shipped.Database.MigrateOnStartup, false, 'starts from appsettings.Docker.json');
  assert.deepEqual(JSON.parse(buildEnvironmentFile(view.context, view.fields, docker.state, { onlyChanges: true }).text), { Paging: { MaxPageSize: 50 } });
});

test('setting a value back to its default is not a change', () => {
  const s = session('Production');
  s.set('Paging:MaxPageSize', 50);
  s.set('Paging:MaxPageSize', 100);
  assert.deepEqual(s.state.values, {});
});

test('in Docker, keys Compose sets from .env are exported to .env, never to a JSON file', () => {
  const s = session('Docker');
  s.set('MultiTenancy:Enabled', true);
  s.set('Paging:MaxPageSize', 50);
  const { context, fields } = s.view();
  const file = buildEnvironmentFile(context, fields, s.state);
  assert.equal(JSON.parse(file.text).MultiTenancy?.Enabled, undefined);
  assert.deepEqual(file.viaDotenv.map(({ field }) => field.path), ['MultiTenancy:Enabled']);
  const variables = buildEnvironmentVariables(context, fields, s.state);
  assert.equal(variables.dotenv, 'MULTITENANCY_ENABLED=true\n');
  assert.match(variables.compose, /^services:\n {2}api:\n {4}environment:\n {6}Paging__MaxPageSize: "50"\n$/);
});

test('a change inside a list of objects writes the whole list; a shortened list warns', () => {
  const s = session('Production');
  s.set('RateLimiting:Policies:3:PermitLimit', 5);
  s.set('Cors:AllowedOrigins', ['https://app.acme.test']);
  const { context, fields } = s.view();
  const file = buildEnvironmentFile(context, fields, s.state);
  const policies = JSON.parse(file.text).RateLimiting.Policies;
  assert.equal(policies.length, 5);
  assert.equal(policies[3].Name, 'auth');
  assert.equal(policies[3].PermitLimit, 5);
  assert.ok(file.warnings.some((warning) => warning.path === 'Cors:AllowedOrigins'));
  const variables = buildEnvironmentVariables(context, fields, s.state);
  assert.match(variables.dotenv, /^RateLimiting__Policies__3__PermitLimit=5$/m);
  assert.match(variables.dotenv, /^Cors__AllowedOrigins__0=https:\/\/app\.acme\.test$/m);
});

test('the full file follows the chosen modules and carries every change', () => {
  const s = session('Production', { agentic: false, search: false, sample: false });
  s.set('Messaging:Transport', 'PostgreSql');
  const { context, fields } = s.view();
  const full = JSON.parse(buildFullFile(context, fields, s.state).text);
  assert.equal(full.Messaging.Transport, 'PostgreSql');
  for (const section of ['Agentic', 'Search', 'Catalog']) assert.equal(full[section], undefined, section);
  assert.equal(full.Hangfire.Servers.Agentic, undefined);
  assert.equal(full.JwtSettings.SecretKey, 'CHANGE_ME_TO_A_RANDOM_SECRET_OF_AT_LEAST_32_CHARS', 'secrets stay placeholders');
});

test('secrets are never stored, shared or exported', () => {
  const s = session('Docker');
  const { byPath } = s.view();
  const secret = byPath.get('JwtSettings:SecretKey');
  assert.equal(secret.secret, true);
  assert.equal(secret.control, 'secret');
  s.apply((state) => withValue(state, secret, 'a-real-secret-value'));
  assert.deepEqual(s.state.values, {});
  const crafted = decodeState(catalog, encodeState({ ...s.state, values: { 'JwtSettings:SecretKey': 'leak', 'Redis:Password': 'leak' } }));
  assert.deepEqual(crafted.values, {});
  const smuggled = sanitizeState(s.view().fields, { ...s.state, values: { 'Attachment:Storage:AzureBlob:ConnectionString': 'leak', 'Paging:MaxPageSize': 5 } });
  assert.deepEqual(smuggled.values, { 'Paging:MaxPageSize': 5 });
  const { context, fields } = s.view();
  const imported = importSettings(context, fields, s.state, '{ "JwtSettings": { "SecretKey": "leak" }, "Paging": { "MaxPageSize": 7 } }');
  assert.deepEqual(imported.state.values, { 'Paging:MaxPageSize': 7 });
  assert.deepEqual(imported.skippedSecrets, ['JwtSettings:SecretKey']);
  for (const text of [buildEnvironmentFile(context, fields, imported.state).text, buildEnvironmentVariables(context, fields, imported.state).dotenv, encodeState(imported.state)]) assert.doesNotMatch(text, /leak/);
});

test('the secrets list names the Docker secret file and how it is mounted', () => {
  const docker = session('Docker');
  let view = docker.view();
  let secrets = buildSecrets(view.context, view.fields, docker.state);
  const files = secrets.needed.map((field) => field.secretFile);
  assert.ok(files.includes('jwt_secret') && files.includes('identity_otp_hmac_key') && files.includes('postgres_password'));
  assert.match(secrets.script, /openssl rand -base64 48 .* > secrets\/jwt_secret\.txt/);
  assert.doesNotMatch(secrets.script, /hangfire_dashboard_password/, 'Compose takes it from HANGFIRE_DASHBOARD_PASSWORD');
  assert.doesNotMatch(secrets.script, /PreviousSecretKeys/, 'optional and not mounted');
  assert.match(secrets.compose, /smtp_password:\n {4}file: \.\/secrets\/smtp_password\.txt/);
  docker.set('Messaging:Transport', 'RabbitMq');
  view = docker.view();
  secrets = buildSecrets(view.context, view.fields, docker.state);
  assert.ok(secrets.needed.some((field) => field.secretFile === 'rabbitmq_password'));
  assert.doesNotMatch(secrets.compose, /rabbitmq_password/, 'docker-compose.rabbitmq.yml mounts it');
  const production = session('Production');
  view = production.view();
  secrets = buildSecrets(view.context, view.fields, production.state);
  assert.ok(secrets.placeholderOnly.includes('Messaging:RabbitMq:Password'), 'CHANGE_ME in the base file must be overridden even when unused');
  const development = session('Development');
  view = development.view();
  assert.match(buildSecrets(view.context, view.fields, development.state).script, /^dotnet user-secrets set "/m);
});

test('production rules: what stops the API from starting, per environment', () => {
  assert.deepEqual(ids(session('Docker').rules(), 'blocking'), [], 'the shipped Docker setup boots');
  assert.deepEqual(ids(session('Development').rules(), 'blocking'), [], 'Development is relaxed');
  const production = session('Production');
  assert.ok(ids(production.rules(), 'blocking').includes('transport-inmemory'));
  production.set('Messaging:Transport', 'RabbitMq');
  assert.ok(ids(production.rules(), 'blocking').includes('rabbitmq-host'));
  production.set('Messaging:RabbitMq:Host', 'rabbitmq.internal');
  production.set('Subscriptions:PaymentGateways:Stripe:Enabled', true);
  assert.ok(ids(production.rules(), 'blocking').includes('gateway-sandbox-Stripe'));
  production.set('Subscriptions:PaymentGateways:Stripe:AllowSandboxInProduction', true);
  production.set('Agentic:Enabled', true);
  assert.deepEqual(ids(production.rules(), 'blocking'), ['agentic-scripted']);
  production.set('Search:Engine', 'Meilisearch');
  assert.ok(ids(production.rules(), 'blocking').includes('meilisearch-url'));
  const docker = session('Docker');
  docker.set('Agentic:Enabled', true);
  assert.ok(ids(docker.rules(), 'warning').includes('agentic-scripted'), 'only IsProduction() refuses Scripted');
  docker.set('AppOptions:Name', 'CHANGE_ME');
  assert.ok(ids(docker.rules(), 'blocking').includes('placeholders'));
});

test('recommendations follow the production guidance', () => {
  const s = session('Production', { multitenancy: 'shared' });
  const warnings = ids(s.rules(), 'warning');
  for (const id of ['migrate-on-startup', 'virus-scanning', 'rls-bypass', 'cors-localhost', 'example-hosts']) assert.ok(warnings.includes(id), id);
  s.set('MultiTenancy:Database:RlsBypass', 'Role');
  s.set('Attachment:Validation:RequireVirusScanning', true);
  const after = s.rules();
  assert.ok(!ids(after).includes('rls-bypass'));
  assert.ok(ids(after, 'blocking').includes('scanner-missing'), 'required scanning without a scanner fails closed');
});

test('visibility follows the controlling setting', () => {
  const s = session('Docker');
  let { byPath } = s.view();
  assert.equal(isVisible(byPath.get('Attachment:Storage:S3:BucketName'), byPath, s.state), false);
  assert.equal(isVisible(byPath.get('Hangfire:Servers:Default:WorkerCount'), byPath, s.state), true);
  s.set('Attachment:Storage:Provider', 'S3');
  s.set('Hangfire:Servers:Default:Enabled', false);
  ({ byPath } = s.view());
  assert.equal(isVisible(byPath.get('Attachment:Storage:S3:BucketName'), byPath, s.state), true);
  assert.equal(isVisible(byPath.get('Hangfire:Servers:Default:WorkerCount'), byPath, s.state), false);
});

test('dictionary entries: an added provider is exported whole; a removed shipped entry needs the full file', () => {
  const s = session('Production');
  s.apply((state) => addEntry(state, 'Agentic:Providers', 'openai'));
  s.set('Agentic:Providers:openai:Endpoint', 'https://api.openai.com/v1');
  let { context, fields, byPath } = s.view();
  assert.equal(byPath.get('Agentic:Providers:openai:ApiKey').secretFile, 'agentic_openai_api_key');
  assert.ok(byPath.get('Agentic:Models:default:Provider').options.some((option) => option.value === 'openai'));
  const provider = JSON.parse(buildEnvironmentFile(context, fields, s.state).text).Agentic.Providers.openai;
  assert.deepEqual(provider, { Kind: 'OpenAiCompatible', Endpoint: 'https://api.openai.com/v1', TimeoutSeconds: 120, MaxRetries: 2 }, 'ApiKey stays a secret');
  s.apply((state) => removeEntry(state, 'Agentic:Providers', 'scripted'));
  ({ context, fields } = s.view());
  assert.ok(buildEnvironmentFile(context, fields, s.state).warnings.some((warning) => warning.path === 'Agentic:Providers:scripted'));
  assert.equal(JSON.parse(buildFullFile(context, fields, s.state).text).Agentic.Providers.scripted, undefined);
  s.apply((state) => removeEntry(state, 'Agentic:Providers', 'openai'));
  assert.equal(Object.keys(s.state.values).some((path) => path.includes('openai')), false);
});

test('share links round-trip and reject anything else', () => {
  const s = session('Production', { search: false, multitenancy: 'dedicated' });
  s.set('Paging:MaxPageSize', 42);
  s.apply((state) => addEntry(state, 'WhatsApp:Templates', 'order_shipped'));
  const decoded = decodeState(catalog, encodeState(s.state));
  assert.deepEqual(decoded, s.state);
  assert.equal(decodeState(catalog, 'not-base64!'), null);
  const bad = decodeState(catalog, Buffer.from(JSON.stringify({ v: 1, s: { search: 'yes', multitenancy: 'galaxy' }, e: 'Mars', c: [] })).toString('base64url'));
  assert.equal(bad.environment, 'Docker');
  assert.equal(bad.symbols.multitenancy, 'off');
  assert.equal(bad.symbols.search, true);
});

test('import: known keys become changes, unknown keys are reported, bad JSON fails clearly', () => {
  const s = session('Production');
  const { context, fields } = s.view();
  const result = importSettings(context, fields, s.state, '// comment\n{ "Messaging": { "Transport": "PostgreSql" }, "Paging": { "MaxPageSize": 100 }, "Nope": { "X": 1 }, }');
  assert.deepEqual(result.state.values, { 'Messaging:Transport': 'PostgreSql' });
  assert.equal(result.imported, 1);
  assert.deepEqual(result.unknown, ['Nope:X']);
  assert.throws(() => importSettings(context, fields, s.state, '{ "a": '), /imported file/);
  assert.throws(() => importSettings(context, fields, s.state, '[1, 2]'), /JSON object/);
});

test('the dotnet new command carries every template choice', () => {
  const command = dotnetNewCommand(catalog, { ...initialState(catalog).symbols, agentic: false, storage: 'S3' }, 'Acme');
  assert.equal(command, 'dotnet new starterkit -n Acme --sample true --search true --agentic false --multitenancy off --transport PostgreSql --storage S3');
});

test('display helpers', () => {
  assert.equal(formatBytes(10485760), '10 MB');
  assert.equal(formatBytes(65537), '65,537 bytes');
  assert.equal(describeCron('*/15 * * * *'), 'Every 15 minutes');
  assert.equal(describeCron('30 3 * * *'), 'Every day at 03:30 UTC');
  assert.equal(describeCron('0 */6 * * *'), 'Every 6 hours');
  assert.equal(describeCron('0 0 1 * *'), undefined);
  assert.equal(describeTimespan('1.00:00:00'), '1 day');
  assert.equal(describeTimespan('00:00:15'), '15 seconds');
  assert.deepEqual(tokenize('{"a": 1}', 'json').map(([kind]) => kind), ['punct', 'key', 'punct', 'text', 'number', 'punct']);
  assert.deepEqual(tokenize('A=b # note\n', 'shell').map(([kind]) => kind), ['variable', 'punct', 'string', 'comment', 'text']);
});
