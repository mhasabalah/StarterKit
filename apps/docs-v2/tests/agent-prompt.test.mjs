import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { startPrompt, pagePrompt } from '../lib/agent-prompt.mjs';

const docs = 'https://docs.example.com/preview';
const read = (file) => readFile(new URL(`../../../${file}`, import.meta.url), 'utf8');

test('the start prompt quotes the real quickstart, setup and guardrail commands', async () => {
  const prompt = startPrompt(docs);
  const [home, guide, template] = await Promise.all([
    readFile(new URL('../app/(home)/page.tsx', import.meta.url), 'utf8'), read('wiki/Getting-Started/New-Project.md'), read('.template.config/template.json'),
  ]);
  // The commands match the home page's quickstart block.
  for (const command of ['dotnet new install StarterKit.Templates', 'dotnet new starterkit -n Acme']) {
    assert.ok(prompt.includes(command), command);
    assert.ok(home.includes(command), `home quickstart: ${command}`);
  }
  // Setup and verification follow the New project guide.
  for (const fact of ['cp .env.example .env', 'CHANGE_ME', 'secrets/README.md', 'dotnet build Acme.slnx', 'dotnet test src/Api.Tests', 'dotnet new starterkit-module -n']) {
    assert.ok(prompt.includes(fact), fact);
    assert.ok(guide.includes(fact), `New-Project.md: ${fact}`);
  }
  // The solution is renamed from sourceName, and the agent files the prompt names ship with the product.
  assert.match(template, /"sourceName": "StarterKit"/);
  assert.ok(!/"CLAUDE\.md"/.test(template), 'CLAUDE.md is never excluded from generated products');
  assert.match(template, /"aiTooling"[\s\S]*?"defaultValue": "true"/);
  // Links resolve under the reader's base path.
  assert.ok(prompt.includes(`${docs}/llms.txt`) && prompt.includes(`${docs}/getting-started/templates/#install`));
  assert.ok(prompt.trimEnd().endsWith('My goal:'));
});

test('the page prompt wraps the guide with its source, the rules and a task slot', () => {
  const prompt = pagePrompt({ docs: `${docs}/`, title: 'Messaging', path: '/architecture/messaging/', markdown: '\n# Messaging\n\nUse the outbox.\n' });
  assert.ok(prompt.includes('Source: https://docs.example.com/preview/architecture/messaging/'));
  assert.ok(prompt.includes('<guide>\n# Messaging\n\nUse the outbox.\n</guide>'));
  assert.ok(prompt.includes('Read CLAUDE.md'));
  assert.ok(prompt.trimEnd().endsWith('My task:'));
});
