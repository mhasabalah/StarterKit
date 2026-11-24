// The configuration builder's model: layering, the field list, visibility and every export. Pure functions over the
// catalog (scripts/config-catalog.mjs) and the overlay, so tests/config-builder-model.test.mjs covers them directly.
//
// Paths are configuration keys (`Section:Sub:Key`). Arrays of objects flatten by index (`RateLimiting:Policies:3:Name`)
// as ASP.NET Core configuration does; arrays of values are one field. A state holds only what the reader changed.
import { loadSettings, loadEnvFile, evaluateWith, parseJsonc } from './jsonc.mjs';
import { modules, fields as fieldSpecs, extras, dictionaries, environments, secretLeafPattern, requiredSecrets } from './overlay.mjs';

/**
 * @typedef {import('./overlay.mjs').FieldSpec} FieldSpec
 * @typedef {import('./overlay.mjs').Condition} Condition
 * @typedef {import('./jsonc.mjs').Symbols} Symbols
 * @typedef {{ files: Record<string, string>, symbols: any[], docs: Record<string, [string, string | null]>,
 *   sections: Record<string, { anchor: string, options?: string, guide?: string }>, secrets: Record<string, string>,
 *   composeSecrets: Record<string, string>, composeEnvironment: { path: string, variable: string | null, fallback: string }[] }} Catalog
 * @typedef {{ symbols: Symbols, environment: string, values: Record<string, unknown>, removed: string[], added: string[] }} State
 */

export const PLACEHOLDER = 'CHANGE_ME';
const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const clone = (value) => (value === undefined ? undefined : structuredClone(value));
export const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const leafOf = (path) => path.slice(path.lastIndexOf(':') + 1);
const parentOf = (path) => (path.includes(':') ? path.slice(0, path.lastIndexOf(':')) : '');
const sectionOf = (path) => path.split(':')[0];

// ── Layering ──────────────────────────────────────────────────────────────────────────────────────────────────────

/** ASP.NET Core layering: objects merge by key, arrays merge by index, anything else is replaced. */
export function mergeConfig(base, override) {
  if (override === undefined) return clone(base);
  if (isObject(base) && isObject(override)) {
    const result = clone(base);
    for (const [key, value] of Object.entries(override)) result[key] = mergeConfig(base[key], value);
    return result;
  }
  if (Array.isArray(base) && Array.isArray(override)) {
    const result = clone(base);
    override.forEach((value, index) => { result[index] = mergeConfig(base[index], value); });
    return result;
  }
  return clone(override);
}

/**
 * Leaves of a configuration tree in file order. Arrays of objects flatten by index; arrays of values are leaves.
 * @returns {{ leaves: Map<string, unknown>, objectArrays: Set<string> }}
 */
export function flatten(tree, prefix = '', leaves = new Map(), objectArrays = new Set()) {
  for (const [key, value] of Object.entries(tree ?? {})) {
    const path = prefix ? `${prefix}:${key}` : key;
    if (isObject(value)) flatten(value, path, leaves, objectArrays);
    else if (Array.isArray(value) && value.some(isObject)) {
      objectArrays.add(path);
      value.forEach((item, index) => (isObject(item) ? flatten(item, `${path}:${index}`, leaves, objectArrays) : leaves.set(`${path}:${index}`, item)));
    } else leaves.set(path, clone(value));
  }
  return { leaves, objectArrays };
}

export function getPath(tree, path) {
  let node = tree;
  for (const segment of path.split(':')) {
    if (node === null || typeof node !== 'object') return undefined;
    node = node[segment];
  }
  return node;
}

/** Sets a value, creating objects (or array slots, when the parent already is an array) on the way. */
export function setPath(tree, path, value) {
  const segments = path.split(':');
  let node = tree;
  segments.slice(0, -1).forEach((segment) => {
    if (node[segment] === null || typeof node[segment] !== 'object') node[segment] = {};
    node = node[segment];
  });
  node[segments.at(-1)] = clone(value);
}

export function deletePath(tree, path) {
  const parent = getPath(tree, parentOf(path)) ?? (parentOf(path) ? undefined : tree);
  if (parent && typeof parent === 'object') delete parent[leafOf(path)];
}

/** A value read from an environment variable, typed like the JSON value it replaces. */
export function coerce(raw, like) {
  if (typeof like === 'boolean') return /^true$/i.test(raw);
  if (typeof like === 'number' && raw !== '' && !Number.isNaN(Number(raw))) return Number(raw);
  return raw;
}

// ── Context: one choice of template symbols and environment ──────────────────────────────────────────────────────

export function defaultSymbols(catalog) {
  return Object.fromEntries(catalog.symbols.map((symbol) => [symbol.id, symbol.default]));
}

/** @returns {State} */
export function initialState(catalog) {
  return { symbols: defaultSymbols(catalog), environment: 'Docker', values: {}, removed: [], added: [] };
}

export function isModuleIncluded(module, symbols) {
  return !module.include || evaluateWith(module.include, symbols);
}

export function moduleOfSection(section) {
  return modules.find((module) => module.sections.includes(section));
}

/**
 * Everything that follows from the symbols and the environment: the rendered files and the baseline each field
 * starts from (appsettings.json, then the environment file, then, in Docker, the variables Compose sets).
 * @param {Catalog} catalog
 * @param {Symbols} symbols
 * @param {string} environmentId
 */
export function createContext(catalog, symbols, environmentId) {
  const environment = environments.find((entry) => entry.id === environmentId) ?? environments[0];
  const baseTree = loadSettings(catalog.files.base, symbols, 'appsettings.json');
  const fileName = `appsettings.${environment.id}.json`;
  const environmentTree = environment.file ? loadSettings(catalog.files[environment.file], symbols, fileName) : {};
  const tree = mergeConfig(baseTree, environmentTree);
  /** @type {Map<string, { variable: string | null, value: unknown }>} */
  const compose = new Map();
  /** @type {Record<string, string>} */
  let dotenv = {};
  if (environment.id === 'Docker') {
    dotenv = loadEnvFile(catalog.files.env, symbols);
    for (const binding of catalog.composeEnvironment) {
      const current = getPath(tree, binding.path);
      if (current === undefined && !getPath(baseTree, sectionOf(binding.path))) continue; // a section this product has not got
      const raw = binding.variable ? dotenv[binding.variable] ?? binding.fallback : binding.fallback;
      const value = coerce(raw, current);
      compose.set(binding.path, { variable: binding.variable, value });
      setPath(tree, binding.path, value);
    }
  }
  const base = flatten(baseTree);
  const merged = flatten(tree);
  return {
    catalog, symbols, environment, fileName, baseTree, environmentTree, tree, compose, dotenv,
    baseLeaves: base.leaves, environmentLeaves: flatten(environmentTree).leaves,
    objectArrays: new Set([...base.objectArrays, ...merged.objectArrays]),
    leaves: merged.leaves,
    includedModules: modules.filter((module) => isModuleIncluded(module, symbols)),
  };
}

// ── Field specs ──────────────────────────────────────────────────────────────────────────────────────────────────

const compiled = fieldSpecs.map(([pattern, spec]) => ({
  regex: new RegExp(`^${pattern.split(':').map((segment) => segment.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '([^:]*)')).join(':')}$`),
  spec,
}));

const substitute = (condition, captures) => ({ ...condition, path: condition.path.replace(/\$(\d)/g, (_, index) => captures[Number(index) - 1] ?? '') });

/**
 * Every overlay entry that matches the path, merged in order (later wins); `$1` in a condition is the first `*`.
 * @param {string} path
 * @returns {FieldSpec & { patterns: number }}
 */
export function specFor(path) {
  /** @type {any} */
  const result = { patterns: 0 };
  for (const { regex, spec } of compiled) {
    const match = path.match(regex);
    if (!match) continue;
    result.patterns++;
    const captures = match.slice(1);
    Object.assign(result, spec);
    if (spec.visibleWhen) result.visibleWhen = [spec.visibleWhen].flat().map((condition) => substitute(condition, captures));
  }
  return result;
}

/** "MaxUploadsPerSession" → "Max uploads per session"; "UseOTLPExporter" → "Use OTLP exporter". */
export function humanize(name) {
  const words = name.replace(/_/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2').split(' ').filter(Boolean);
  return words.map((word, index) => (/^[A-Z0-9]{2,}$/.test(word) ? word : index === 0 ? word[0].toUpperCase() + word.slice(1).toLowerCase() : word.toLowerCase())).join(' ');
}

const unitFor = (leaf) => (/Bytes$/.test(leaf) ? 'bytes' : /(Milliseconds|Ms)$/.test(leaf) ? 'ms' : /Seconds$/.test(leaf) ? 'seconds' : /Minutes$/.test(leaf) ? 'minutes' : /Hours$/.test(leaf) ? 'hours' : /Days$/.test(leaf) ? 'days' : /Percent$/.test(leaf) ? '%' : undefined);
const timespan = /^(\d+\.)?\d{1,2}:\d{2}:\d{2}(\.\d+)?$/;

function controlFor(spec, value, leaf) {
  if (spec.control) return spec.control;
  if (typeof value === 'boolean') return 'boolean';
  if (typeof value === 'number') return 'number';
  if (Array.isArray(value)) return 'tags';
  if (typeof value === 'string' && timespan.test(value)) return 'timespan';
  if (/Cron$/.test(leaf)) return 'cron';
  return 'text';
}

/** The dictionary a path belongs to, if any: `Agentic:Providers:openai:Kind` → { path: 'Agentic:Providers', entry: 'openai' }. */
export function dictionaryOf(path) {
  for (const [dictionary, definition] of Object.entries(dictionaries)) {
    if (!path.startsWith(`${dictionary}:`)) continue;
    const entry = path.slice(dictionary.length + 1).split(':')[0];
    return { path: dictionary, entry, definition };
  }
  return undefined;
}

/** Docs for a key: exact, else the same key under another dictionary entry or array index, else none. */
function docFor(catalog, path) {
  if (catalog.docs[path]) return catalog.docs[path];
  const dictionary = dictionaryOf(path);
  if (dictionary) {
    const rest = path.slice(dictionary.path.length + dictionary.entry.length + 2);
    const sibling = Object.keys(catalog.docs).find((key) => key.startsWith(`${dictionary.path}:`) && key.endsWith(`:${rest}`) && key.split(':').length === path.split(':').length);
    if (sibling) return catalog.docs[sibling];
  }
  const indexed = path.replace(/:\d+(?=:|$)/g, ':0');
  if (catalog.docs[indexed]) return catalog.docs[indexed];
  for (const [key, doc] of Object.entries(catalog.docs)) {
    if (key.includes('*') && new RegExp(`^${key.replace(/\*/g, '[^:]+')}$`).test(path)) return doc;
  }
  return undefined;
}

export function secretFileFor(catalog, path, spec) {
  if (spec.secretFile) return spec.secretFile;
  if (catalog.secrets[path]) return catalog.secrets[path];
  for (const [pattern, file] of Object.entries(catalog.secrets)) {
    if (!pattern.includes('<')) continue;
    const regex = new RegExp(`^${pattern.replace(/<[^>]+>/g, '([^:]+)')}$`);
    const match = path.match(regex);
    if (match) return file.replace(/<[^>]+>/, match[1].toLowerCase());
  }
  return path.split(':').join('__');
}

/**
 * @typedef {object} Field
 * @property {string} path
 * @property {string} section
 * @property {string} moduleId
 * @property {string} label
 * @property {string} group  parent path, for grouping
 * @property {unknown} defaultValue  the baseline for this environment
 * @property {'base' | 'environment' | 'compose' | 'option'} origin  where the baseline comes from
 * @property {string | null} [composeVariable]
 * @property {string} control
 * @property {{ value: string, label?: string, hint?: string }[]} [options]
 * @property {'array' | 'csv'} [format]
 * @property {string} [unit]
 * @property {number} [min]
 * @property {number} [max]
 * @property {number} [step]
 * @property {boolean} secret
 * @property {string} [secretFile]
 * @property {'random' | 'external'} [secretKind]
 * @property {boolean} [optional]  a secret that may stay empty
 * @property {boolean} essential
 * @property {Condition[]} visibleWhen
 * @property {string} description
 * @property {string | null} guide
 * @property {string} [hint]
 * @property {{ path: string, entry: string, removable: boolean }} [dictionary]
 * @property {boolean} [added]  an entry the reader added
 */

/**
 * The field list: every leaf of the layered files, the option-only extras, the missing fields of dictionary entries
 * and the entries the reader added, minus removed entries. File order, with additions placed beside their siblings.
 * @param {ReturnType<typeof createContext>} context
 * @param {State} state
 * @returns {Field[]}
 */
export function buildFields(context, state) {
  const { catalog, leaves } = context;
  /** @type {Map<string, { value: unknown, origin: Field['origin'] }>} */
  const entries = new Map();
  const removed = (path) => state.removed.some((prefix) => path === prefix || path.startsWith(`${prefix}:`));
  for (const [path, value] of leaves) {
    if (removed(path)) continue;
    const origin = context.compose.has(path) ? 'compose' : context.environmentLeaves.has(path) ? 'environment' : 'base';
    entries.set(path, { value, origin });
  }
  const order = [...entries.keys()];
  const place = (path) => {
    if (entries.has(path)) return;
    let prefix = parentOf(path);
    while (prefix) {
      const index = order.findLastIndex((key) => key.startsWith(`${prefix}:`));
      if (index >= 0) { order.splice(index + 1, 0, path); return; }
      prefix = parentOf(prefix);
    }
    order.push(path);
  };
  const hasSection = (path) => context.baseTree[sectionOf(path)] !== undefined;
  for (const extra of extras) {
    if (!hasSection(extra.path) || removed(extra.path) || entries.has(extra.path)) continue;
    const dictionary = dictionaryOf(extra.path);
    if (dictionary && getPath(context.tree, `${dictionary.path}:${dictionary.entry}`) === undefined) continue;
    place(extra.path);
    entries.set(extra.path, { value: extra.default, origin: 'option' });
  }
  // Entries the reader added to a dictionary exist only in state.values.
  for (const path of Object.keys(state.values)) {
    if (entries.has(path) || removed(path) || !dictionaryOf(path) || !hasSection(path)) continue;
    place(path);
    entries.set(path, { value: undefined, origin: 'option' });
  }
  // Every entry of a dictionary gets the fields of its item shape (e.g. Endpoint on a provider that only sets Kind).
  for (const [dictionary, definition] of Object.entries(dictionaries)) {
    if (!hasSection(dictionary)) continue;
    const names = new Set([...order].map((path) => dictionaryOf(path)).filter((entry) => entry?.path === dictionary).map((entry) => entry.entry));
    for (const name of names) {
      for (const [key, value] of Object.entries(definition.item)) {
        const path = `${dictionary}:${name}:${key}`;
        if (entries.has(path) || removed(path)) continue;
        place(path);
        entries.set(path, { value: added(state, path) ? undefined : value, origin: 'option' });
      }
    }
  }
  const extraByPath = new Map(extras.map((extra) => [extra.path, extra]));
  return order.filter((path) => entries.has(path)).map((path) => {
    const { value, origin } = entries.get(path);
    const spec = specFor(path);
    const leaf = leafOf(path);
    const section = sectionOf(path);
    const owner = moduleOfSection(section);
    const doc = docFor(catalog, path);
    const extra = extraByPath.get(path);
    const dictionary = dictionaryOf(path);
    const secret = spec.secret ?? secretLeafPattern.test(path);
    const isAdded = added(state, path);
    const itemValue = dictionary ? clone(dictionary.definition.item[leafOf(path)]) : undefined;
    // An added entry has no baseline: every one of its fields is a change.
    const defaultValue = isAdded ? undefined : value === undefined ? itemValue : value;
    const sample = defaultValue ?? itemValue ?? state.values[path];
    const options = spec.optionsFrom ? dictionaryEntries(context, state, spec.optionsFrom).map((name) => ({ value: name })) : spec.options;
    return {
      path, section, moduleId: owner?.id ?? 'Platform', label: humanize(leaf), group: parentOf(path),
      defaultValue, origin, composeVariable: context.compose.get(path)?.variable ?? null,
      control: secret ? 'secret' : controlFor(spec, sample, leaf), options, format: spec.format,
      unit: typeof sample === 'number' ? unitFor(leaf) : undefined, min: spec.min, max: spec.max, step: spec.step,
      secret, secretFile: secret ? secretFileFor(catalog, path, spec) : undefined, secretKind: spec.secretKind,
      optional: Boolean(spec.optional), essential: Boolean(spec.essential), visibleWhen: spec.visibleWhen ?? [],
      description: extra?.description ?? doc?.[0] ?? '', guide: extra?.guide ?? doc?.[1] ?? catalog.sections[section]?.guide ?? null,
      hint: spec.hint,
      dictionary: dictionary ? { path: dictionary.path, entry: dictionary.entry, removable: Boolean(dictionary.definition.removable) } : undefined,
      added: isAdded,
    };
  });
}

/** Whether a path belongs to a dictionary entry the reader added (it has no baseline). */
function added(state, path) {
  const dictionary = dictionaryOf(path);
  return Boolean(dictionary && (state.added ?? []).includes(`${dictionary.path}:${dictionary.entry}`));
}

/** Names of the entries of a dictionary as the reader sees them (baseline plus added, minus removed). */
export function dictionaryEntries(context, state, dictionary) {
  const names = new Set(Object.keys(getPath(context.tree, dictionary) ?? {}));
  for (const path of Object.keys(state.values)) {
    const entry = dictionaryOf(path);
    if (entry?.path === dictionary) names.add(entry.entry);
  }
  for (const prefix of state.removed) if (prefix.startsWith(`${dictionary}:`)) names.delete(prefix.slice(dictionary.length + 1));
  return [...names];
}

// ── Values and visibility ────────────────────────────────────────────────────────────────────────────────────────

/** @param {Field} field @param {State} state */
export function valueOf(field, state) {
  return Object.hasOwn(state.values, field.path) ? state.values[field.path] : field.defaultValue;
}

/** @param {Field} field @param {State} state */
export function isChanged(field, state) {
  return Object.hasOwn(state.values, field.path) && !same(state.values[field.path], field.defaultValue);
}

/** Sets (or, when it equals the baseline, clears) a value. Secrets are never stored. */
export function withValue(state, field, value) {
  if (field.secret) return state;
  const values = { ...state.values };
  if (same(value, field.defaultValue) && !field.added) delete values[field.path];
  else values[field.path] = clone(value);
  return { ...state, values };
}

/** Adds an entry to a dictionary, seeded with its item shape. */
export function addEntry(state, dictionary, name) {
  const definition = dictionaries[dictionary];
  const key = `${dictionary}:${name}`;
  const values = { ...state.values };
  // Secret fields of the shape (an ApiKey) are never stored: they are supplied as secrets.
  for (const [leaf, value] of Object.entries(definition.item)) if (!secretLeafPattern.test(`${key}:${leaf}`)) values[`${key}:${leaf}`] = clone(value);
  return { ...state, values, added: [...(state.added ?? []), key], removed: state.removed.filter((prefix) => prefix !== key) };
}

/** Removes an entry: an added one disappears, a shipped one is marked removed (the full export drops it). */
export function removeEntry(state, dictionary, name) {
  const key = `${dictionary}:${name}`;
  const values = Object.fromEntries(Object.entries(state.values).filter(([path]) => !path.startsWith(`${key}:`)));
  const wasAdded = (state.added ?? []).includes(key);
  return { ...state, values, added: (state.added ?? []).filter((entry) => entry !== key), removed: wasAdded ? state.removed : [...state.removed, key] };
}

/** Drops anything a state must not hold: values of secret fields (e.g. from a crafted share link). */
export function sanitizeState(fields, state) {
  const secret = new Set(fields.filter((field) => field.secret).map((field) => field.path));
  const values = Object.fromEntries(Object.entries(state.values).filter(([path]) => !secret.has(path) && !secretLeafPattern.test(path)));
  return { ...state, values };
}

export function lookup(fields) {
  return new Map(fields.map((field) => [field.path, field]));
}

/** @param {Condition} condition */
function holds(condition, byPath, state) {
  const field = byPath.get(condition.path);
  const value = field ? valueOf(field, state) : undefined;
  const text = value === undefined || value === null ? '' : String(value).toLowerCase();
  if (condition.truthy) return value === true || text === 'true';
  if ('equals' in condition) return text === String(condition.equals).toLowerCase();
  if ('not' in condition) return text !== String(condition.not).toLowerCase();
  if (condition.in) return condition.in.some((option) => String(option).toLowerCase() === text);
  return true;
}

/** @param {Field} field */
export function isVisible(field, byPath, state) {
  return field.visibleWhen.every((condition) => holds(condition, byPath, state));
}

/** Changed fields, in field order. */
export function changes(fields, state) {
  return fields.filter((field) => isChanged(field, state)).map((field) => ({ field, value: valueOf(field, state) }));
}

// ── Exports ──────────────────────────────────────────────────────────────────────────────────────────────────────

const stringify = (tree) => JSON.stringify(tree, null, 2) + '\n';

/** The array-of-objects path a field sits in (`RateLimiting:Policies`), if any. */
function objectArrayOf(context, path) {
  for (const array of context.objectArrays) if (path.startsWith(`${array}:`)) return array;
  return undefined;
}

/** Reader-facing notes about what an override cannot express. */
function overrideWarnings(context, state, list) {
  const warnings = [];
  for (const { field, value } of list) {
    const base = context.baseLeaves.get(field.path);
    if (Array.isArray(value) && Array.isArray(base) && value.length < base.length) {
      warnings.push({ path: field.path, text: `${field.path} is shorter than the list in appsettings.json. An override replaces entries by position, so entries ${value.length}–${base.length - 1} of the base list still apply. Use the full appsettings.json export for this list.` });
    }
  }
  for (const prefix of state.removed) {
    if (getPath(context.baseTree, prefix) !== undefined) warnings.push({ path: prefix, text: `${prefix} was removed. An override file cannot remove an entry of appsettings.json; use the full export.` });
  }
  return warnings;
}

/** Applies the changes to a tree; a change inside an array of objects writes the whole array. */
function applyChanges(context, tree, list, effective) {
  const arrays = new Set();
  for (const { field, value } of list) {
    const array = objectArrayOf(context, field.path);
    if (array) { arrays.add(array); continue; }
    setPath(tree, field.path, value);
  }
  for (const array of arrays) setPath(tree, array, getPath(effective, array));
}

/** The configuration as the reader has it: the layered tree with every change applied. */
export function effectiveTree(context, fields, state) {
  const tree = clone(context.tree);
  for (const prefix of state.removed) deletePath(tree, prefix);
  for (const field of fields) if (Object.hasOwn(state.values, field.path)) setPath(tree, field.path, state.values[field.path]);
  return tree;
}

/**
 * `appsettings.<Environment>.json`: the shipped environment file with the reader's changes, or only the changes.
 * In Docker, keys Compose sets from `.env` are left out: an environment variable would win over the file.
 */
export function buildEnvironmentFile(context, fields, state, { onlyChanges = false } = {}) {
  const all = changes(fields, state);
  const viaDotenv = all.filter(({ field }) => context.compose.has(field.path));
  const list = all.filter(({ field }) => !context.compose.has(field.path));
  const tree = onlyChanges ? {} : clone(context.environmentTree);
  applyChanges(context, tree, list, effectiveTree(context, fields, state));
  return { fileName: context.fileName, text: stringify(tree), count: list.length, viaDotenv, warnings: overrideWarnings(context, state, list) };
}

/** The whole appsettings.json for the chosen modules, with every change in it. */
export function buildFullFile(context, fields, state) {
  const list = changes(fields, state);
  const tree = clone(context.baseTree);
  for (const prefix of state.removed) deletePath(tree, prefix);
  applyChanges(context, tree, list, effectiveTree(context, fields, state));
  const shadowed = list.filter(({ field }) => field.origin === 'environment' || field.origin === 'compose').map(({ field }) => field.path);
  const warnings = shadowed.length
    ? [{ path: shadowed[0], text: `${context.environment.file ? context.fileName : 'The environment'}${context.compose.size ? ' (or Compose)' : ''} also sets ${shadowed.join(', ')}, and wins over appsettings.json. Change ${shadowed.length === 1 ? 'it' : 'them'} in the environment export.` }]
    : [];
  return { fileName: 'appsettings.json', text: stringify(tree), count: list.length, warnings };
}

/** `Section__Key` → value pairs for one change; a list becomes one variable per position. */
function variablesOf(path, value) {
  const name = path.split(':').join('__');
  if (Array.isArray(value)) return value.map((item, index) => [`${name}__${index}`, item]);
  return [[name, value]];
}
const envValue = (value) => (value === null || value === undefined ? '' : String(value));
const shellQuote = (value) => (/^[\w.:/@+,-]*$/.test(value) ? value : `"${value.replace(/(["\\$`])/g, '\\$1')}"`);

/**
 * Environment variables for the changes. Docker: `.env` lines for the keys Compose maps, and a Compose file for the
 * rest; other targets: `Section__Key=value` lines for the host or orchestrator.
 */
export function buildEnvironmentVariables(context, fields, state) {
  const list = changes(fields, state);
  const warnings = overrideWarnings(context, state, list).filter((warning) => !warning.text.includes('was removed'));
  if (context.environment.id === 'Docker') {
    const dotenv = list.filter(({ field }) => field.composeVariable).map(({ field, value }) => `${field.composeVariable}=${envValue(value)}`);
    const rest = list.filter(({ field }) => !field.composeVariable);
    const compose = rest.length
      ? ['services:', '  api:', '    environment:', ...rest.flatMap(({ field, value }) => variablesOf(field.path, value)).map(([name, value]) => `      ${name}: ${JSON.stringify(envValue(value))}`)].join('\n') + '\n'
      : '';
    return { dotenv: dotenv.length ? dotenv.join('\n') + '\n' : '', compose, count: list.length, warnings };
  }
  const lines = list.flatMap(({ field, value }) => variablesOf(field.path, value)).map(([name, value]) => `${name}=${shellQuote(envValue(value))}`);
  return { dotenv: lines.length ? lines.join('\n') + '\n' : '', compose: '', count: list.length, warnings };
}

/**
 * The secrets this configuration needs: every visible secret field, plus the ones the app requires or whose base
 * value is a placeholder (ProductionConfigurationGuard checks every key, shown or not).
 */
export function secretsNeeded(context, fields, state) {
  const byPath = lookup(fields);
  return fields.filter((field) => field.secret && (
    isVisible(field, byPath, state) || requiredSecrets.includes(field.path) || String(field.defaultValue ?? '').toUpperCase().includes(PLACEHOLDER)));
}

export function buildSecrets(context, fields, state) {
  const needed = secretsNeeded(context, fields, state);
  const byPath = lookup(fields);
  // Listed only because the shipped value is a placeholder: the guard checks every key, used or not.
  const placeholderOnly = needed.filter((field) => !isVisible(field, byPath, state) && !requiredSecrets.includes(field.path)).map((field) => field.path);
  if (context.environment.id === 'Development') {
    const lines = needed.map((field) => `dotnet user-secrets set ${JSON.stringify(field.path)} ${JSON.stringify(field.secretKind === 'random' ? 'GENERATE_A_RANDOM_VALUE' : 'PASTE_THE_VALUE')} --project src/Api`);
    return { needed, placeholderOnly, script: lines.length ? lines.join('\n') + '\n' : '', compose: '' };
  }
  if (context.environment.id === 'Production') {
    const lines = needed.map((field) => `${field.path.split(':').join('__')}=<from your secret store>${field.optional ? '   # optional' : ''}`);
    return { needed, placeholderOnly, script: lines.length ? lines.join('\n') + '\n' : '', compose: '' };
  }
  const mounted = (field) => Boolean(context.catalog.composeSecrets[field.secretFile]);
  // Optional secrets are created (empty) only when a compose file mounts them; otherwise they are just listed.
  const files = [...new Map(needed.filter((field) => !field.composeVariable && (!field.optional || mounted(field))).map((field) => [field.secretFile, field])).values()];
  const line = (field) => (field.optional
    ? `[ -e secrets/${field.secretFile}.txt ] || : > secrets/${field.secretFile}.txt   # optional: ${field.path}`
    : field.secretKind === 'random'
      ? `[ -s secrets/${field.secretFile}.txt ] || openssl rand -base64 48 | tr -d '\\n' > secrets/${field.secretFile}.txt`
      : `printf '%s' 'PASTE_THE_VALUE' > secrets/${field.secretFile}.txt   # ${field.path}`);
  const script = ['mkdir -p secrets', ...files.map(line), 'chmod 600 secrets/*'].join('\n') + '\n';
  const unmounted = files.filter((field) => !mounted(field));
  const compose = unmounted.length
    ? ['services:', '  api:', '    secrets:', ...unmounted.map((field) => `      - ${field.secretFile}`), 'secrets:', ...unmounted.flatMap((field) => [`  ${field.secretFile}:`, `    file: ./secrets/${field.secretFile}.txt`])].join('\n') + '\n'
    : '';
  return { needed, placeholderOnly, script, compose };
}

/** The `dotnet new` command that generates a product with these modules. */
export function dotnetNewCommand(catalog, symbols, name = 'MyProduct') {
  const flags = catalog.symbols.map((symbol) => `--${symbol.id} ${symbols[symbol.id]}`);
  return `dotnet new starterkit -n ${name} ${flags.join(' ')}`;
}

// ── Sharing and import ───────────────────────────────────────────────────────────────────────────────────────────

const toBase64Url = (text) => {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};
const fromBase64Url = (text) => {
  const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/'));
  return new TextDecoder().decode(Uint8Array.from(binary, (char) => char.charCodeAt(0)));
};

/** A compact, secret-free encoding of a state for the URL hash. */
export function encodeState(state) {
  const values = Object.fromEntries(Object.entries(state.values).filter(([path]) => !secretLeafPattern.test(path)));
  return toBase64Url(JSON.stringify({ v: 1, s: state.symbols, e: state.environment, c: values, r: state.removed, a: state.added ?? [] }));
}

/** @returns {State | null} */
export function decodeState(catalog, encoded) {
  try {
    const data = JSON.parse(fromBase64Url(encoded));
    if (data?.v !== 1) return null;
    const symbols = { ...defaultSymbols(catalog) };
    for (const symbol of catalog.symbols) {
      const value = data.s?.[symbol.id];
      if (symbol.type === 'bool' && typeof value === 'boolean') symbols[symbol.id] = value;
      if (symbol.type === 'choice' && symbol.choices.some((choice) => choice.value === value)) symbols[symbol.id] = value;
    }
    const environment = environments.some((entry) => entry.id === data.e) ? data.e : 'Docker';
    const values = Object.fromEntries(Object.entries(isObject(data.c) ? data.c : {}).filter(([path]) => typeof path === 'string' && !secretLeafPattern.test(path)));
    const strings = (list) => (Array.isArray(list) ? list.filter((item) => typeof item === 'string') : []);
    return { symbols, environment, values, removed: strings(data.r), added: strings(data.a) };
  } catch {
    return null;
  }
}

/**
 * Loads an appsettings file the reader pasted or uploaded into the state: known keys become changes, secrets are
 * skipped (never stored), unknown keys are reported.
 */
export function importSettings(context, fields, state, text) {
  const tree = parseJsonc(text, 'imported file');
  if (!isObject(tree)) throw new Error('The file must contain a JSON object.');
  const byPath = lookup(fields);
  const { leaves } = flatten(tree);
  let values = { ...state.values };
  const skippedSecrets = [];
  const unknown = [];
  let imported = 0;
  for (const [path, value] of leaves) {
    const field = byPath.get(path);
    if (field ? field.secret : secretLeafPattern.test(path)) { skippedSecrets.push(path); continue; }
    if (!field) {
      if (dictionaryOf(path)) { values[path] = value; imported++; } else unknown.push(path);
      continue;
    }
    if (same(value, field.defaultValue)) delete values[path];
    else { values[path] = value; imported++; }
  }
  return { state: { ...state, values }, imported, skippedSecrets, unknown };
}
