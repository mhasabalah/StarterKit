// Portable full-template renderer, adapted from V1. Keep semantics covered by tests.
export function evaluate(expression) {
  const tokens = expression.match(/&&|\|\||[!()]|[A-Za-z_][A-Za-z0-9_]*|\S/g) || [];
  let position = 0;
  function atom() {
    const token = tokens[position++];
    if (token === '!') return !atom();
    if (token === '(') {
      const value = or();
      if (tokens[position++] !== ')') throw new Error('Expected closing parenthesis');
      return value;
    }
    if (token && /^[A-Za-z_][A-Za-z0-9_]*$/.test(token)) return true;
    throw new Error(`Unexpected expression token: ${token ?? 'end of expression'}`);
  }
  function and() {
    let value = atom();
    while (tokens[position] === '&&') { position++; const right = atom(); value = value && right; }
    return value;
  }
  function or() {
    let value = and();
    while (tokens[position] === '||') { position++; const right = and(); value = value || right; }
    return value;
  }
  const value = or();
  if (position !== tokens.length) throw new Error(`Unexpected expression token: ${tokens[position]}`);
  return value;
}

export function renderTemplate(text, source) {
  const stack = [];
  const result = [];
  for (const [index, line] of text.replace(/\r\n?/g, '\n').split('\n').entries()) {
    const active = () => stack.every((frame) => frame.active);
    if (!/^\s*<!--#/.test(line)) { if (active()) result.push(line); continue; }
    try {
      const match = line.match(/^\s*<!--#(if|elseif|else|endif)\s*(.*?)-->\s*$/);
      if (!match) throw new Error('Invalid template marker');
      const [, kind, expression] = match;
      if (kind === 'if' || kind === 'elseif') {
        const take = evaluate(expression);
        if (kind === 'if') stack.push({ active: take, taken: take, elseSeen: false });
        else {
          const frame = stack.at(-1);
          if (!frame || frame.elseSeen) throw new Error('Unexpected elseif');
          frame.active = !frame.taken && take;
          frame.taken ||= take;
        }
      } else {
        if (expression) throw new Error(`Unexpected expression after ${kind}`);
        const frame = stack.at(-1);
        if (!frame) throw new Error(`Unexpected ${kind}`);
        if (kind === 'endif') stack.pop();
        else {
          if (frame.elseSeen) throw new Error('Duplicate else');
          frame.active = !frame.taken;
          frame.taken = frame.elseSeen = true;
        }
      }
    } catch (error) { throw new Error(`${source}:${index + 1}: ${error.message}`); }
  }
  if (stack.length) throw new Error(`${source}:${text.split('\n').length}: Unclosed template conditional`);
  // Inline examples are documentation, not directives; Markdown code spans preserve them literally.
  return result.join('\n');
}
