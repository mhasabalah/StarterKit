import { test, expect } from '@playwright/test';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
// The OS clipboard may store CRLF line endings (Windows); compare the text, not the platform's newlines.
const clipboard = (page: import('@playwright/test').Page) =>
  page.evaluate(() => navigator.clipboard.readText()).then((text) => text.replace(/\r\n/g, '\n'));

test.beforeEach(async ({ context }) => { await context.grantPermissions(['clipboard-read', 'clipboard-write']); });

test('home copies a start prompt for an AI agent, identical to its preview', async ({ page }) => {
  await page.goto(base + '/', { waitUntil: 'networkidle' }); // hydrated, so the click reaches React
  const root = await page.evaluate((path) => `${location.origin}${path}`, base);
  // Located by class: the accessible name changes to "Copied" after the click.
  const button = page.locator('.agent-start .agent-copy');
  await expect(button).toHaveAccessibleName('Copy prompt for your AI agent');
  await button.click();
  await expect(button).toHaveText('Copied');
  await expect(page.getByRole('status').filter({ hasText: 'Prompt copied' })).toHaveCount(1);
  const prompt = await clipboard(page);
  expect(prompt).toContain('dotnet new starterkit -n Acme');
  expect(prompt).toContain('Read CLAUDE.md');
  expect(prompt).toContain(`${root}/llms.txt`);
  expect(prompt.trimEnd()).toMatch(/My goal:$/);
  await page.getByText('Preview the prompt').click();
  expect((await page.locator('.agent-preview pre').textContent())?.trim()).toBe(prompt.trim());
  await expect(button).toHaveText('Copy prompt for your AI agent', { timeout: 5000 });
});

test('a docs page copies itself wrapped as an agent prompt', async ({ page }) => {
  await page.goto(base + '/architecture/messaging/', { waitUntil: 'networkidle' });
  const root = await page.evaluate((path) => `${location.origin}${path}`, base);
  const button = page.locator('.page-tools .agent-copy');
  await expect(button).toHaveAccessibleName('Copy for agent');
  await button.click();
  await expect(button).toHaveText('Copied');
  const prompt = await clipboard(page);
  const title = (await page.locator('#nd-page h1').textContent())!.trim();
  expect(prompt).toContain(`Guide: ${title}`);
  expect(prompt).toContain(`Source: ${root}/architecture/messaging/`);
  expect(prompt).toMatch(/<guide>\n[\s\S]*MessagingArchitectureTests[\s\S]*\n<\/guide>/); // the page's own Markdown
  expect(prompt.trimEnd()).toMatch(/My task:$/);
});

test('a failed Markdown request reports the failure instead of copying', async ({ page }) => {
  await page.route('**/llms.mdx/**', (route) => route.fulfill({ status: 500, body: 'error' }));
  await page.goto(base + '/architecture/messaging/', { waitUntil: 'networkidle' });
  const button = page.locator('.page-tools .agent-copy');
  await expect(button).toHaveAccessibleName('Copy for agent');
  await button.click();
  await expect(button).toHaveText('Copy failed');
  await expect(page.getByRole('status').filter({ hasText: 'could not be copied' })).toHaveCount(1);
});
