// Display helpers of the configuration builder: units, cron readings, DOM ids and a tiny highlighter for the three
// output languages (JSON, YAML, shell/.env). Colours come from the site tokens (config-builder.css), not a theme.

/** 10485760 → "10 MB" (binary units, as the defaults are written). */
export function formatBytes(value) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '';
  const units = ['bytes', 'KB', 'MB', 'GB', 'TB'];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1 && size % 1024 === 0) { size /= 1024; unit++; }
  return `${size.toLocaleString('en-US')} ${units[unit]}`;
}

const pad = (value) => String(value).padStart(2, '0');

/** A plain-English reading of the common five-field cron shapes (UTC); undefined for anything else. */
export function describeCron(expression) {
  const parts = String(expression ?? '').trim().split(/\s+/);
  if (parts.length !== 5) return undefined;
  const [minute, hour, day, month, weekday] = parts;
  const everyDay = day === '*' && month === '*' && weekday === '*';
  if (!everyDay) return undefined;
  if (minute === '*' && hour === '*') return 'Every minute';
  const step = (field) => field.match(/^\*\/(\d+)$/)?.[1];
  if (step(minute) && hour === '*') return `Every ${step(minute)} minutes`;
  if (/^\d+$/.test(minute) && step(hour)) return `Every ${step(hour)} hours${minute === '0' ? '' : ` at :${pad(minute)}`}`;
  if (/^\d+$/.test(minute) && hour === '*') return `Every hour at :${pad(minute)}`;
  if (/^\d+$/.test(minute) && /^\d+$/.test(hour)) return `Every day at ${pad(hour)}:${pad(minute)} UTC`;
  return undefined;
}

/** "1.00:00:00" → "1 day"; "00:00:15" → "15 seconds". */
export function describeTimespan(value) {
  const match = String(value ?? '').match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})$/);
  if (!match) return undefined;
  const [, days = '0', hours, minutes, seconds] = match;
  const parts = [[Number(days), 'day'], [Number(hours), 'hour'], [Number(minutes), 'minute'], [Number(seconds), 'second']]
    .filter(([amount]) => amount > 0).map(([amount, unit]) => `${amount} ${unit}${amount === 1 ? '' : 's'}`);
  return parts.length ? parts.join(' ') : 'zero';
}

/** A DOM id for a configuration key. */
export const fieldId = (path) => `cb-f-${path.replace(/[^\w-]/g, '-')}`;
export const sectionId = (section) => `cb-s-${section.replace(/[^\w-]/g, '-')}`;

/**
 * Splits code into [kind, text] tokens. Kinds: key, string, number, literal, comment, punct, variable, text.
 * @param {string} code
 * @param {'json' | 'yaml' | 'shell'} language
 * @returns {[string, string][]}
 */
export function tokenize(code, language) {
  /** @type {[string, string][]} */
  const tokens = [];
  const push = (kind, text) => { if (text) tokens.push([kind, text]); };
  if (language === 'json') {
    const pattern = /("(?:[^"\\]|\\.)*")(\s*:)?|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|\b(true|false|null)\b|([{}[\],])|(\s+)|(.)/g;
    for (const match of code.matchAll(pattern)) {
      if (match[1]) { push(match[2] ? 'key' : 'string', match[1]); push('punct', match[2]); }
      else if (match[3]) push('number', match[3]);
      else if (match[4]) push('literal', match[4]);
      else if (match[5]) push('punct', match[5]);
      else push('text', match[0]);
    }
    return tokens;
  }
  for (const line of code.split(/(?<=\n)/)) {
    const comment = line.match(/^(\s*)(#.*)(\n?)$/);
    if (comment) { push('text', comment[1]); push('comment', comment[2]); push('text', comment[3]); continue; }
    if (language === 'yaml') {
      const match = line.match(/^(\s*)(- )?([\w.-]+)(:)(.*)(\n?)$/);
      if (match) {
        push('text', match[1]); push('punct', match[2]); push('key', match[3]); push('punct', match[4]);
        const value = match[5];
        push(/^\s*"/.test(value) ? 'string' : /^\s*\d+$/.test(value) ? 'number' : 'text', value);
        push('text', match[6]);
        continue;
      }
      const item = line.match(/^(\s*)(- )(.*)(\n?)$/);
      if (item) { push('text', item[1]); push('punct', item[2]); push('string', item[3]); push('text', item[4]); continue; }
      push('text', line);
      continue;
    }
    const assignment = line.match(/^([A-Za-z_][\w]*)(=)(.*?)(\s+#.*)?(\n?)$/);
    if (assignment) {
      push('variable', assignment[1]); push('punct', assignment[2]); push('string', assignment[3]); push('comment', assignment[4]); push('text', assignment[5]);
      continue;
    }
    for (const match of line.matchAll(/('(?:[^'])*'|"(?:[^"\\]|\\.)*")|(\s#.*$)|(\s+)|([^\s'"]+)/g)) {
      if (match[1]) push('string', match[1]);
      else if (match[2]) push('comment', match[2]);
      else push('text', match[0]);
    }
  }
  return tokens;
}
