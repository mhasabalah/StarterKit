/**
 * Prompts a reader pastes into an AI coding agent (Claude Code, Cursor, Copilot...). Every command and file named here
 * exists in the template or the wiki; tests/agent-prompt.test.mjs checks them against `.template.config`, the
 * New project guide and the home quickstart, so the prompt cannot drift from the product.
 *
 * `docs` is the documentation root as the reader sees it (origin plus base path), so links work on any deployment.
 */

/** @param {string} docs @param {string} path */
const link = (docs, path) => `${docs.replace(/\/$/, '')}${path}`;

/** @param {string} docs */
const rules = (docs) => `Rules:
- Read CLAUDE.md in the repository first and follow it; it is the source of truth for conventions, and the architecture tests enforce it. AGENTS.md repeats the rules that must never break. With Claude Code, use the bundled /starterkit:* skills for recurring changes.
- Modules talk only through ISender and another module's .Contracts, or through integration events (transactional outbox). Never reference another module's implementation.
- Never put real credentials, hostnames or IPs in committed files; use CHANGE_ME and api.example.com.
- Use the commands and names from the documentation exactly; do not invent APIs. When unsure, read the guide: ${link(docs, '/llms.txt')} lists every page as Markdown, and ${link(docs, '/llms-full.txt')} holds them all in one file.
- After code changes, run dotnet build on the product's .slnx, the relevant tests and dotnet test src/Api.Tests (architecture guardrails), and fix every failure before you finish.`;

/** The home page prompt: start a new product with an agent. @param {string} docs @returns {string} */
export function startPrompt(docs) {
  return `You are helping me build a product on StarterKit, a production-ready ASP.NET Core 10 modular monolith template (identity, optional multi-tenancy, attachments, subscriptions and billing, notifications, background jobs, outbox messaging, .NET Aspire).

Steps:
1. Create the product (replace Acme with my product name):
   dotnet new install StarterKit.Templates
   dotnet new starterkit -n Acme
   If the template package feed is not configured, follow ${link(docs, '/getting-started/templates/#install')}.
2. In the new folder, run cp .env.example .env and replace every CHANGE_ME, then create secrets/*.txt as secrets/README.md describes.
3. Check the build: dotnet build Acme.slnx, then dotnet test src/Api.Tests.
4. Add each business capability as a module: dotnet new starterkit-module -n <Name>. The full walkthrough is ${link(docs, '/getting-started/new-project/')}.

${rules(docs)}

My goal: `;
}

/**
 * @typedef {object} ConfigPromptInput
 * @property {string} docs
 * @property {string} productName
 * @property {string} command                       The `dotnet new starterkit` command for the chosen modules.
 * @property {{ id: string, label: string }} environment
 * @property {{ path: string, language: string, text: string }[]} files   Files to write, relative to the product root.
 * @property {{ title: string, language: string, text: string }[]} additions  Lines to add (.env, a Compose file, variables).
 * @property {{ list: string[], script: string, language: string }} secrets  Secret names and where each goes; never a value.
 * @property {string[]} blocking                     Checks that still stop the API from starting.
 * @property {string[]} recommendations              Open checks the reader chose not to fix.
 */

/** @param {string} language @param {string} text */
const fence = (language, text) => `\`\`\`${language}\n${text.trimEnd()}\n\`\`\``;

/**
 * The guided setup's hand-over: apply a configuration chosen in the configuration builder. It carries only what the
 * builder exports (no secret values: the exports never contain them) plus the commands that check the result.
 * @param {ConfigPromptInput} input @returns {string}
 */
export function configPrompt({ docs, productName, command, environment, files, additions, secrets, blocking, recommendations }) {
  const steps = [];
  steps.push(`If the product does not exist yet, create it with exactly these modules and options:
   dotnet new install StarterKit.Templates
   ${command}
   If the template package feed is not configured, follow ${link(docs, '/getting-started/templates/#install')}. If the product already exists, do not generate it again: compare its modules with these options and tell me about any difference.`);
  for (const file of files) {
    steps.push(`Write ${file.path} with exactly this content. If the file exists, show me the difference before you replace it.
${fence(file.language, file.text)}`);
  }
  for (const addition of additions) steps.push(`${addition.title}
${fence(addition.language, addition.text)}`);
  if (secrets.list.length) {
    steps.push(`Set up these secrets. Never commit them, never print their values, and never ask me to paste a secret into this chat:
${secrets.list.map((line) => `   - ${line}`).join('\n')}${secrets.script.trim() ? `
   Run this from the product root. Lines with PASTE_THE_VALUE need a value only I have (a provider key or password): leave them as they are and list them for me at the end.
${fence(secrets.language, secrets.script)}` : ''}`);
  }
  steps.push(`Check the build: dotnet build ${productName}.slnx, then dotnet test src/Api.Tests.`);
  if (environment.id === 'Docker') steps.push('Start the stack with docker compose up -d and confirm the API is healthy. ProductionConfigurationGuard stops the API at startup when a production rule is broken; if it does, fix the setting it names.');
  else if (environment.id === 'Production') steps.push('These settings target ASPNETCORE_ENVIRONMENT=Production. ProductionConfigurationGuard stops the API at startup when a production rule is broken; tell me what to set on the host, and fix the setting it names if a test start fails.');
  else steps.push('Run the API locally (dotnet run --project src/Api, or the Aspire AppHost) and confirm it starts.');

  return `I am setting up a product built from StarterKit, an ASP.NET Core 10 modular monolith template. I chose its configuration in the StarterKit configuration builder (${link(docs, '/tools/config-builder/')}). Apply it exactly.

Target environment: ${environment.label} (ASPNETCORE_ENVIRONMENT=${environment.id}).

Steps:
${steps.map((step, index) => `${index + 1}. ${step}`).join('\n')}
${blocking.length ? `
These checks still stop the API from starting in this environment. Tell me before you deploy, and fix them only as I decide:
${blocking.map((line) => `- ${line}`).join('\n')}
` : ''}${recommendations.length ? `
Recommendations I have not applied yet (mention them when you finish; do not change them without asking):
${recommendations.map((line) => `- ${line}`).join('\n')}
` : ''}
${rules(docs)}

When you finish, list what you changed and every secret I still have to fill in.
`;
}

/**
 * A documentation page wrapped for an agent: what the product is, the rules, the page as Markdown, and a slot for the
 * task. @param {{ docs: string, title: string, path: string, markdown: string }} page @returns {string}
 */
export function pagePrompt({ docs, title, path, markdown }) {
  return `I am working in a product built from StarterKit, an ASP.NET Core 10 modular monolith template. Use the guide below to help with my task.

${rules(docs)}

Guide: ${title}
Source: ${link(docs, path)}

<guide>
${markdown.trim()}
</guide>

My task: `;
}
