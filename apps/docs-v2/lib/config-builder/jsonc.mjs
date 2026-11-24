// Renders the template-conditional appsettings files the way `dotnet new` does, for a chosen set of template symbols.
//
// The JSON files carry `//#if (expr)` / `//#elseif` / `//#else` / `//#endif` markers. Inside a conditional block a
// line that starts with `//` (and is not a marker) is a commented-out JSON line that `dotnet new` uncomments when its
// branch is taken, e.g. `//"Enabled": true,` under `#else` of `multitenancy == "off"`. Lines of branches not taken
// are dropped. The result is parsed as JSON after trailing commas are removed.

/**
 * @typedef {Record<string, string | boolean>} Symbols  Template symbol values: booleans, or the chosen value of a choice.
 */

/**
 * Evaluates a template expression against symbol values: identifiers, string literals, `==`, `!=`, `!`, `&&`, `||`
 * and parentheses. An identifier alone is true when its symbol is `true` (choices are compared with `==`).
 * @param {string} expression
 * @param {Symbols} symbols
 * @returns {boolean}
 */
export function evaluateWith(expression, symbols) {
  const tokens = expression.match(/&&|\|\||==|!=|[!()]|"[^"]*"|[A-Za-z_][A-Za-z0-9_]*|\S/g) || [];
  let position = 0;
  const peek = () => tokens[position];
  function value(token) {
    if (token === undefined) throw new Error('Unexpected end of expression');
    if (token.startsWith('"')) return token.slice(1, -1);
    if (/^(true|false)$/.test(token)) return token === 'true';
    if (/^[A-Za-z_][A-Za-z0-9_]*$/.test(token)) {
      if (!(token in symbols)) throw new Error(`Unknown template symbol: ${token}`);
      return symbols[token];
    }
    throw new Error(`Unexpected expression token: ${token}`);
  }
  function comparison() {
    const token = tokens[position++];
    if (token === '!') return !comparison();
    if (token === '(') {
      const inner = or();
      if (tokens[position++] !== ')') throw new Error('Expected closing parenthesis');
      return inner;
    }
    const left = value(token);
    if (peek() === '==' || peek() === '!=') {
      const operator = tokens[position++];
      const right = value(tokens[position++]);
      const equal = String(left).toLowerCase() === String(right).toLowerCase();
      return operator === '==' ? equal : !equal;
    }
    return left === true || left === 'true';
  }
  function and() {
    let result = comparison();
    while (peek() === '&&') { position++; const right = comparison(); result = result && right; }
    return result;
  }
  function or() {
    let result = and();
    while (peek() === '||') { position++; const right = and(); result = result || right; }
    return result;
  }
  const result = or();
  if (position !== tokens.length) throw new Error(`Unexpected expression token: ${tokens[position]}`);
  return result;
}

const styles = {
  // JSON: `//#if (x)`; a toggled line is any `//` line inside a block.
  json: { marker: /^\s*\/\/#(if|elseif|else|endif)\b\s*(.*?)\s*$/, toggled: /^\s*\/\/(?!#)/, uncomment: /^(\s*)\/\/\s?/ },
  // .env: `#if (x)`; a toggled line is a commented assignment (`#NAME=value`), never a prose comment.
  env: { marker: /^\s*#(if|elseif|else|endif)\b\s*(.*?)\s*$/, toggled: /^\s*#[A-Z][A-Z0-9_]*=/, uncomment: /^(\s*)#/ },
};

/**
 * Applies the template markers of a JSONC file for the given symbols and returns plain JSONC text.
 * @param {string} text
 * @param {Symbols} symbols
 * @param {string} [source] file name used in errors
 */
export function renderJsonc(text, symbols, source = 'appsettings') {
  return renderConditional(text, symbols, source, 'json');
}

/**
 * Applies the template markers of a `.env` file for the given symbols and returns its variables.
 * @param {string} text
 * @param {Symbols} symbols
 * @returns {Record<string, string>}
 */
export function loadEnvFile(text, symbols, source = '.env.example') {
  /** @type {Record<string, string>} */
  const variables = {};
  for (const line of renderConditional(text, symbols, source, 'env').split('\n')) {
    const match = line.match(/^\s*([A-Z][A-Z0-9_]*)=(.*)$/);
    if (match) variables[match[1]] = match[2].trim().replace(/^(['"])(.*)\1$/, '$2');
  }
  return variables;
}

/**
 * @param {string} text
 * @param {Symbols} symbols
 * @param {string} source
 * @param {'json' | 'env'} style
 */
function renderConditional(text, symbols, source, style) {
  const { marker, toggled, uncomment } = styles[style];
  /** @type {{ active: boolean, taken: boolean, elseSeen: boolean, parentActive: boolean }[]} */
  const stack = [];
  const output = [];
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  for (const [index, line] of lines.entries()) {
    const active = stack.every((frame) => frame.active);
    const match = line.match(marker);
    if (!match) {
      if (!active) continue;
      // A commented-out JSON line inside a taken branch is uncommented, as dotnet new does.
      output.push(stack.length && toggled.test(line) ? line.replace(uncomment, '$1') : line);
      continue;
    }
    const [, kind, expression] = match;
    try {
      if (kind === 'if') {
        const take = active && evaluateWith(expression, symbols);
        stack.push({ active: take, taken: take, elseSeen: false, parentActive: active });
      } else if (kind === 'elseif') {
        const frame = stack.at(-1);
        if (!frame || frame.elseSeen) throw new Error('Unexpected elseif');
        const take = frame.parentActive && !frame.taken && evaluateWith(expression, symbols);
        frame.active = take;
        frame.taken ||= take;
      } else if (kind === 'else') {
        const frame = stack.at(-1);
        if (!frame || frame.elseSeen) throw new Error('Unexpected else');
        frame.active = frame.parentActive && !frame.taken;
        frame.taken = frame.elseSeen = true;
      } else {
        if (!stack.length) throw new Error('Unexpected endif');
        stack.pop();
      }
    } catch (error) {
      throw new Error(`${source}:${index + 1}: ${/** @type {Error} */ (error).message}`);
    }
  }
  if (stack.length) throw new Error(`${source}: unclosed template conditional`);
  return output.join('\n');
}

/**
 * Parses JSON with `//` line comments and trailing commas (what ASP.NET Core's JSON configuration provider accepts).
 * Comments and commas inside strings are preserved.
 * @param {string} text
 * @param {string} [source]
 */
export function parseJsonc(text, source = 'appsettings') {
  let result = '';
  let inString = false;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (inString) {
      result += char;
      if (char === '\\') { result += text[++index] ?? ''; continue; }
      if (char === '"') inString = false;
      continue;
    }
    if (char === '"') { inString = true; result += char; continue; }
    if (char === '/' && text[index + 1] === '/') {
      while (index < text.length && text[index] !== '\n') index++;
      result += '\n';
      continue;
    }
    if (char === '/' && text[index + 1] === '*') {
      index = text.indexOf('*/', index + 2);
      if (index < 0) throw new Error(`${source}: unclosed block comment`);
      index++;
      continue;
    }
    if (char === ',') {
      // Drop the comma when the next significant character closes the object or array.
      let next = index + 1;
      while (next < text.length && /\s/.test(text[next])) next++;
      if (text[next] === '}' || text[next] === ']') continue;
      if (text[next] === '/' && text[next + 1] === '/') {
        let after = next;
        while (true) {
          while (after < text.length && text[after] !== '\n') after++;
          while (after < text.length && /\s/.test(text[after])) after++;
          if (text[after] === '/' && text[after + 1] === '/') continue;
          break;
        }
        if (text[after] === '}' || text[after] === ']') continue;
      }
    }
    result += char;
  }
  try {
    return JSON.parse(result);
  } catch (error) {
    throw new Error(`${source}: ${/** @type {Error} */ (error).message}`);
  }
}

/**
 * Renders and parses one appsettings file for the given symbols.
 * @param {string} text
 * @param {Symbols} symbols
 * @param {string} [source]
 * @returns {Record<string, unknown>}
 */
export function loadSettings(text, symbols, source) {
  return parseJsonc(renderJsonc(text, symbols, source), source);
}
