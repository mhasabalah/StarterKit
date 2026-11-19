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
