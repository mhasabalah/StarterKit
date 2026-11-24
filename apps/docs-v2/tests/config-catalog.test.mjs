// Drift guards of the configuration builder: the hand-written overlay must keep matching the real appsettings files,
// the C# enums, the template and the wiki. A failure here means a key, an enum member or a page changed.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { buildConfigCatalog } from '../scripts/config-catalog.mjs';
import { pages, pageSlug } from '../../../docs-site/sidebar.mjs';
import { evaluateWith, renderJsonc, parseJsonc, loadEnvFile } from '../lib/config-builder/jsonc.mjs';
import { modules, fields as specs, extras, dictionaries, environments, decisions, decisionLabels } from '../lib/config-builder/overlay.mjs';
import { createContext, buildFields, flatten, specFor, initialState } from '../lib/config-builder/model.mjs';
import { tools } from '../lib/tools.mjs';
import { guideSteps, stepKeys, handover, clampStep, guidePathOf, applyScript, questionHelp } from '../lib/config-builder/guide.mjs';
import { buildEnvironmentFile, buildEnvironmentVariables, buildSecrets, withValue } from '../lib/config-builder/model.mjs';
import { evaluateRules } from '../lib/config-builder/rules.mjs';

const catalog = await buildConfigCatalog();
const routes = new Set(pages.map((page) => `/${pageSlug(page.source)}/`));
const allOn = { sample: true, agentic: true, search: true, multitenancy: 'dedicated', transport: 'RabbitMq', storage: 'S3' };
const allOff = { sample: false, agentic: false, search: false, multitenancy: 'off', transport: 'PostgreSql', storage: 'Local' };
const state = (symbols, environment) => ({ ...initialState(catalog), symbols, environment });

/** Every field of every environment, for the largest product. */
function everyField() {
  const result = new Map();
  for (const environment of environments) {
    const context = createContext(catalog, allOn, environment.id);
    for (const field of buildFields(context, state(allOn, environment.id))) result.set(field.path, field);
  }
  return result;
}
const all = everyField();

test('template conditions render like dotnet new: toggled lines, else branches and nesting', () => {
  assert.equal(evaluateWith('multitenancy == "off"', { multitenancy: 'off' }), true);
  assert.equal(evaluateWith('storage != "Local" && !sample', { storage: 'S3', sample: false }), true);
  assert.throws(() => evaluateWith('unknown', {}), /Unknown template symbol/);
  const text = '{\n  //#if (mt == "off")\n  "Enabled": false,\n  //#else\n  //"Enabled": true,\n  //#endif\n  //#if (a)\n  "A": 1,\n  //#endif\n}';
  assert.deepEqual(parseJsonc(renderJsonc(text, { mt: 'off', a: false })), { Enabled: false });
  assert.deepEqual(parseJsonc(renderJsonc(text, { mt: 'shared', a: true })), { Enabled: true, A: 1 });
  assert.deepEqual(parseJsonc('{ "url": "http://x//y", "list": [1, 2,], }'), { url: 'http://x//y', list: [1, 2] });
  assert.equal(loadEnvFile('#if (t == "RabbitMq")\n#T=RabbitMq\n#else\nT=PostgreSql\n#endif\n# a prose comment', { t: 'RabbitMq' }).T, 'RabbitMq');
});

test('every template choice of the real files renders and parses', () => {
  for (const symbols of [allOn, allOff, { ...allOn, multitenancy: 'shared', storage: 'AzureBlob' }]) {
    for (const environment of environments) assert.doesNotThrow(() => createContext(catalog, symbols, environment.id), `${JSON.stringify(symbols)} ${environment.id}`);
  }
  const on = createContext(catalog, allOn, 'Docker');
  const off = createContext(catalog, allOff, 'Docker');
  assert.ok(on.tree.Agentic && on.tree.Hangfire.Servers.Agentic && on.tree.Search && on.tree.Catalog);
  assert.ok(!off.tree.Agentic && !off.tree.Hangfire.Servers.Agentic && !off.tree.Search && !off.tree.Catalog);
  assert.equal(on.tree.Attachment.Storage.Provider, 'S3');
  assert.equal(on.compose.get('MultiTenancy:DefaultIsolation').value, 'Dedicated');
  assert.equal(on.compose.get('Messaging:Transport').value, 'RabbitMq');
  assert.equal(off.compose.get('MultiTenancy:Enabled').value, false);
});

test('the module picker mirrors .template.config/template.json', async () => {
  const template = JSON.parse(await readFile('../../.template.config/template.json', 'utf8'));
  for (const symbol of catalog.symbols) {
    const definition = template.symbols[symbol.id];
    assert.ok(definition, `template symbol ${symbol.id}`);
    if (symbol.type === 'choice') assert.deepEqual(symbol.choices.map((choice) => choice.value), definition.choices.map((choice) => choice.choice));
  }
  for (const owner of modules.filter((entry) => entry.include)) assert.doesNotThrow(() => evaluateWith(owner.include, allOn), owner.id);
});

test('every section of the real files belongs to exactly one module', () => {
  const sections = new Set();
  for (const environment of environments) {
    const context = createContext(catalog, allOn, environment.id);
    for (const path of flatten(context.tree).leaves.keys()) sections.add(path.split(':')[0]);
  }
  for (const section of sections) {
    const owners = modules.filter((module) => module.sections.includes(section));
    assert.equal(owners.length, 1, `${section} is owned by ${owners.map((owner) => owner.id).join(', ') || 'no module'}`);
  }
  const listed = modules.flatMap((module) => module.sections);
  for (const section of listed) assert.ok(sections.has(section), `overlay lists section ${section}, which no appsettings file has`);
});

test('every overlay pattern, extra and dictionary names keys that exist', () => {
  const paths = [...all.keys()];
  for (const [pattern] of specs) {
    const regex = new RegExp(`^${pattern.split(':').map((segment) => segment.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '[^:]*')).join(':')}$`);
    assert.ok(paths.some((path) => regex.test(path)), `overlay pattern ${pattern} matches no key`);
  }
  for (const extra of extras) assert.ok(all.has(extra.path), `extra ${extra.path}`);
  for (const key of decisions.flatMap((group) => group.keys)) assert.ok(all.has(key), `decision ${key}`);
  for (const key of Object.keys(decisionLabels)) assert.ok(decisions.some((group) => group.keys.includes(key)), `label for ${key}, which is not a decision`);
  for (const dictionary of Object.keys(dictionaries)) assert.ok(paths.some((path) => path.startsWith(`${dictionary}:`)), `dictionary ${dictionary}`);
});

test('enum options equal the C# enum members they name', async () => {
  const seen = new Set();
  for (const [pattern, spec] of specs) {
    if (!spec.enumSource || seen.has(spec.enumSource)) continue;
    seen.add(spec.enumSource);
    const [file, name] = spec.enumSource.split('#');
    const source = await readFile(`../../${file}`, 'utf8');
    const body = source.match(new RegExp(`enum ${name}\\s*\\{([\\s\\S]*?)\\}`))?.[1];
    assert.ok(body, `${spec.enumSource} not found`);
    const members = body.replace(/\/\/\/?.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/\[[^\]]*\]/g, '').split(',').map((member) => member.split('=')[0].trim()).filter(Boolean);
    assert.deepEqual(spec.options.map((option) => option.value).filter(Boolean).sort(), [...members].sort(), `${pattern} vs ${spec.enumSource}`);
  }
  assert.ok(seen.size >= 15, 'enum-backed fields');
});

test('every enum field starts on one of its options', () => {
  for (const field of all.values()) {
    if (field.control !== 'enum' || !field.options?.length || specFor(field.path).optionsFrom) continue;
    const value = field.defaultValue ?? '';
    assert.ok(field.options.some((option) => option.value === value), `${field.path} = ${JSON.stringify(value)} is not an option`);
  }
});

test('every field is documented and links to a real page', () => {
  for (const field of all.values()) {
    assert.ok(field.description, `${field.path} has no description in the configuration pages`);
    assert.ok(field.guide, `${field.path} has no guide`);
    if (field.guide.startsWith('/')) assert.ok(routes.has(field.guide.split('#')[0]), `${field.path} links to ${field.guide}`);
  }
  for (const section of Object.values(catalog.sections)) assert.ok(routes.has(section.anchor.split('#')[0]), section.anchor);
});

test('hand-written links of the builder are real pages', async () => {
  const text = (await readFile('lib/config-builder/rules.mjs', 'utf8')) + (await readFile('lib/config-builder/overlay.mjs', 'utf8'));
  for (const [, route] of text.matchAll(/(?:guide|url): '(\/[^']*)'/g)) assert.ok(routes.has(route), route);
  for (const tool of tools) assert.ok(!routes.has(tool.url), `${tool.url} collides with a wiki page`);
});

test('secrets: every secret file of Secrets.md and the compose files is known', () => {
  assert.equal(catalog.secrets['JwtSettings:SecretKey'], 'jwt_secret');
  assert.equal(catalog.composeSecrets.jwt_secret, 'docker-compose.yml');
  assert.equal(catalog.composeSecrets.rabbitmq_password, 'docker-compose.rabbitmq.yml');
  const secretFields = [...all.values()].filter((field) => field.secret);
  assert.ok(secretFields.length >= 15);
  for (const field of secretFields) assert.match(field.secretFile, /^[\w]+$/, field.path);
  assert.equal(all.get('Agentic:Providers:scripted:ApiKey')?.secretFile, 'agentic_scripted_api_key');
});

test('the guided setup asks every key decision once, and only keys that exist', () => {
  const asked = guideSteps.flatMap((step) => stepKeys(step));
  assert.equal(new Set(asked).size, asked.length, 'a key is asked in two steps');
  for (const key of asked) assert.ok(all.has(key), `guide step key ${key}`);
  const omitted = guideSteps.flatMap((step) => step.omit ?? []);
  for (const group of decisions) for (const key of group.keys) assert.ok(asked.includes(key) || omitted.includes(key), `decision ${key} is in no step`);
  for (const step of guideSteps) for (const id of step.groups) assert.ok(decisions.some((group) => group.id === id), `${step.id} names group ${id}`);
  for (const key of asked) assert.ok(questionHelp[key], `guide question ${key} has no plain help`);
  for (const key of Object.keys(questionHelp)) assert.ok(asked.includes(key), `help for ${key}, which no step asks`);
  assert.deepEqual([clampStep('3'), clampStep(99), clampStep('x'), guidePathOf('agent'), guidePathOf('other')], [3, guideSteps.length - 1, 0, 'agent', null]);
});

test('the guide hand-over carries files, .env lines and secrets by name, never a secret value', () => {
  let current = { ...state({ ...allOn, transport: 'PostgreSql' }, 'Docker') };
  const context = createContext(catalog, current.symbols, 'Docker');
  let fields = buildFields(context, current);
  const set = (path, value) => { current = withValue(current, fields.find((field) => field.path === path), value); fields = buildFields(context, current); };
  set('Messaging:Transport', 'RabbitMq');
  set('Email:Provider', 'Smtp');
  const outputs = {
    environment: { id: 'Docker', label: 'Docker (Compose)', relaxed: false },
    environmentFile: buildEnvironmentFile(context, fields, current),
    variables: buildEnvironmentVariables(context, fields, current),
    secrets: buildSecrets(context, fields, current),
  };
  const parts = handover({ catalog, outputs, issues: evaluateRules(context, fields, current) });
  assert.equal(parts.files[0].path, 'src/Api/appsettings.Docker.json');
  assert.match(parts.files[0].text, /"Provider": "Smtp"/);
  assert.match(parts.additions[0].text, /^MESSAGING_TRANSPORT=RabbitMq$/m);
  assert.ok(parts.secrets.list.some((line) => line.startsWith('JwtSettings:SecretKey: Supplied by secrets/jwt_secret.txt')));
  assert.match(parts.secrets.script, /openssl rand/);
  // Secret leaves may appear only empty or as the shipped CHANGE_ME placeholder.
  const written = [...parts.files, ...parts.additions].map((part) => part.text).join('\n');
  for (const leaf of new Set(fields.filter((entry) => entry.secret).map((field) => field.path.split(':').at(-1)))) {
    for (const [, value] of written.matchAll(new RegExp(`"${leaf}": "([^"]*)"`, 'g'))) assert.ok(value === '' || value.includes('CHANGE_ME'), `${leaf} = ${value}`);
  }
});

test('the apply script writes each file, merges .env by key and keeps backups', () => {
  const parts = {
    files: [{ path: 'src/Api/appsettings.Docker.json', language: 'json', text: '{\n  "Email": { "Provider": "Smtp" }\n}\n' }],
    additions: [{ title: '.env', language: 'shell', text: 'MESSAGING_TRANSPORT=RabbitMq\nREDIS_ENABLED=true\n' }],
    secrets: { list: ['JwtSettings:SecretKey: Supplied by secrets/jwt_secret.txt.'], script: 'mkdir -p secrets\nchmod 600 secrets/*\n', language: 'shell' },
    blocking: [], recommendations: [],
  };
  const script = applyScript(parts, { id: 'Docker', label: 'Docker (Compose)' });
  assert.ok(script.startsWith('#!/usr/bin/env bash\n'));
  assert.match(script, /set -euo pipefail/);
  assert.match(script, /cp 'src\/Api\/appsettings\.Docker\.json' 'src\/Api\/appsettings\.Docker\.json\.bak'/);
  assert.match(script, /cat > 'src\/Api\/appsettings\.Docker\.json' <<'STARTERKIT_EOF'\n\{\n {2}"Email"/);
  assert.match(script, /grep -v -E '\^\(MESSAGING_TRANSPORT\|REDIS_ENABLED\)=' \.env\.bak > \.env/);
  assert.match(script, /chmod 600 secrets\/\* 2>\/dev\/null \|\| true/);
  // Every here-document is closed.
  assert.equal(script.match(/<<'STARTERKIT_EOF'/g).length, script.match(/^STARTERKIT_EOF$/gm).length);
  const production = applyScript({ ...parts, additions: [] }, { id: 'Production', label: 'Production' });
  assert.match(production, /echo '  JwtSettings__SecretKey'/);
  assert.ok(!production.includes('mkdir -p secrets'));
});
