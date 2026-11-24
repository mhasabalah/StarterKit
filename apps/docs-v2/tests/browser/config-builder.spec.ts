import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const url = `${base}/tools/config-builder/`;
const axe = (page: Page) => new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();

async function open(page: Page, width: number, theme = 'light', hash = '#builder') {
  await page.setViewportSize({ width, height: 1000 });
  // Steps slide in; checks read the settled screen (reduced motion turns the entrance off).
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.addInitScript((value) => { localStorage.setItem('theme', value); localStorage.removeItem('starterkit-config-builder:v1'); }, theme);
  await page.goto(url + hash);
  await expect(page.getByRole('heading', { level: 1, name: 'Configuration builder' })).toBeVisible();
}

for (const theme of ['light', 'dark']) {
  for (const width of [390, 1440]) {
    test(`configuration builder in ${theme} at ${width}px`, async ({ page }) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await open(page, width, theme);
      if (width === 390) {
        await expect(page.getByRole('button', { name: 'Settings', exact: true })).toHaveAttribute('aria-pressed', 'true');
        await page.getByRole('button', { name: 'Product', exact: true }).click();
      }
      await expect(page.getByRole('list', { name: 'Modules in this product' }).getByRole('listitem')).toHaveCount(11);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await axe(page)).violations).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
}

test('modules, environments and checks drive the settings and the export', async ({ page, context }) => {
  await open(page, 1440);
  const settings = page.getByRole('main', { name: 'Settings' });
  await expect(settings.locator('#cb-s-Agentic')).toHaveCount(1);
  const agentic = page.getByRole('button', { name: /^Agentic: included/ });
  await agentic.click();
  await expect(page.getByRole('button', { name: /^Agentic: left out/ })).toHaveAttribute('aria-pressed', 'false');
  await expect(settings.locator('#cb-s-Agentic')).toHaveCount(0);
  await expect(settings.locator('#cb-f-Hangfire-Servers-Agentic-Enabled')).toHaveCount(0);
  await expect(page.locator('.cb-command')).toContainText('--agentic false');

  // Key decisions come first; sections stay folded until opened.
  await expect(settings.getByRole('heading', { level: 2, name: 'Key decisions' })).toBeVisible();
  await settings.getByLabel('File storage').selectOption('S3');
  const attachment = settings.locator('#cb-s-Attachment-toggle');
  await expect(attachment).toHaveAttribute('aria-expanded', 'false');
  await expect(settings.locator('#cb-f-Attachment-Storage-S3-BucketName')).toHaveCount(0);
  await attachment.click();
  await expect(attachment).toHaveAttribute('aria-expanded', 'true');
  await expect(settings.locator('#cb-f-Attachment-Storage-S3-BucketName')).toBeVisible();

  await page.getByRole('radio', { name: 'Production (custom host)' }).check({ force: true });
  const checks = page.getByRole('complementary', { name: 'Checks and export' });
  const blocking = checks.locator('.cb-issue[data-severity="blocking"]').filter({ hasText: 'In-memory messaging' });
  await expect(blocking).toBeVisible();
  await blocking.getByRole('button', { name: 'Messaging:Transport' }).click();
  await expect(settings.locator('#cb-f-Messaging-Transport-row')).toBeInViewport();
  await settings.locator('#cb-f-Messaging-Transport-row').getByRole('radio', { name: 'PostgreSQL' }).check();
  await expect(blocking).toHaveCount(0);
  await expect(checks.locator('.cb-check-summary')).toContainText('Nothing blocks startup');

  const output = checks.locator('.cb-tab-panel pre');
  await expect(output).toContainText('"Transport": "PostgreSql"');
  await expect(output).toContainText('"Provider": "S3"');
  await expect(checks.locator('.cb-code-title')).toHaveText('appsettings.Production.json');
  await checks.getByRole('tab', { name: 'Variables' }).click();
  await expect(output).toContainText('Messaging__Transport=PostgreSql');
  await checks.getByRole('tab', { name: /Secrets/ }).click();
  await expect(checks.locator('.cb-secret-list')).toContainText('Attachment:Storage:S3:SecretAccessKey');
  await expect(checks.locator('.cb-tab-panel')).not.toContainText('PostgreSql"');

  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.getByRole('button', { name: /Copy a link to this configuration/ }).click();
  const link = await page.evaluate(() => navigator.clipboard.readText());
  expect(link).toContain('#s=');
  const fresh = await context.newPage();
  await fresh.addInitScript(() => localStorage.removeItem('starterkit-config-builder:v1'));
  await fresh.goto(link);
  await expect(fresh.getByRole('radio', { name: 'Production (custom host)' })).toBeChecked();
  await expect(fresh.getByRole('button', { name: /^Agentic: left out/ })).toBeVisible();
  await expect(fresh.locator('.cb-head-count')).toContainText('2 changes');
});

test('a documentation page leads into the builder at its section', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(`${base}/attachment/configuration/`);
  const callout = page.getByRole('complementary', { name: 'Configuration builder' });
  await expect(callout).toContainText('Attachment');
  await callout.getByRole('link', { name: 'Open the configuration builder' }).click();
  await expect(page).toHaveURL(/\/tools\/config-builder\/#section=Attachment$/);
  await expect(page.locator('#cb-s-Attachment-toggle')).toBeFocused();
  await expect(page.locator('#cb-s-Attachment-toggle')).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('navigation', { name: 'Sections' }).getByRole('button', { name: /^Attachment/ })).toHaveAttribute('aria-current', 'true');
  await page.goto(`${base}/reference/configuration/`);
  await expect(page.getByRole('complementary', { name: 'Configuration builder' }).getByRole('link')).toHaveAttribute('href', /\/tools\/config-builder\/$/);
});

test('filters, the ordered channel list and dictionary entries', async ({ page }) => {
  await open(page, 1440);
  const settings = page.getByRole('main', { name: 'Settings' });
  await settings.getByRole('searchbox').fill('channel order');
  await expect(settings.locator('.cb-row')).toHaveCount(1);
  await settings.getByRole('button', { name: 'Move Email up' }).click();
  await expect(settings.locator('.cb-ordered-name').first()).toHaveText('Email');
  await expect(page.locator('.cb-head-count')).toContainText('1 change');
  await settings.getByRole('searchbox').fill('');
  await settings.getByRole('checkbox', { name: 'Changed only' }).check();
  await expect(settings.locator('.cb-row')).toHaveCount(1);
  await settings.getByRole('button', { name: /Reset Notification:DefaultChannelOrder/ }).click();
  await expect(settings.getByText(/Nothing is changed yet/)).toBeVisible();
  await settings.getByRole('checkbox', { name: 'Changed only' }).uncheck();

  await settings.getByLabel('AI agents').check();
  await settings.getByRole('button', { name: 'Expand all' }).click();
  await expect(settings.locator('.cb-section-toggle[aria-expanded="false"]')).toHaveCount(0);
  await settings.getByRole('button', { name: 'Collapse all' }).click();
  await settings.getByRole('button', { name: 'Open the Agentic section' }).click();
  await expect(settings.locator('#cb-s-Agentic-toggle')).toHaveAttribute('aria-expanded', 'true');
  // Opening a section moves focus to its toggle a tick later; wait for it, so Enter below reaches the form.
  await expect(settings.locator('#cb-s-Agentic-toggle')).toBeFocused();
  const add = settings.getByRole('textbox', { name: 'Add a provider' });
  await add.fill('openai');
  await add.press('Enter');
  await expect(settings.locator('#cb-f-Agentic-Providers-openai-Endpoint')).toBeVisible();
  await expect(settings.locator('#cb-f-Agentic-Providers-openai-ApiKey')).toContainText('agentic_openai_api_key');
  await add.fill('openai');
  await add.press('Enter');
  await expect(settings.getByRole('alert')).toContainText('A provider named openai exists');
});

for (const theme of ['light', 'dark']) {
  for (const width of [390, 1440]) {
    test(`guided setup in ${theme} at ${width}px`, async ({ page }) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await open(page, width, theme, '');
      // A first visit starts the guide at its first question.
      await expect(page.getByRole('heading', { level: 2, name: 'How will you apply it?' })).toBeVisible();
      expect((await axe(page)).violations).toEqual([]);
      await page.getByRole('button', { name: /^Set it up by hand/ }).click();
      await expect(page).toHaveURL(/#guided=manual&step=1$/);
      await expect(page.getByRole('heading', { level: 2, name: 'Where will it run?' })).toBeFocused();
      await page.getByRole('button', { name: /^Next: Your product/ }).click();
      await expect(page.getByRole('list', { name: 'Modules in this product' }).getByRole('listitem')).toHaveCount(11);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await axe(page)).violations).toEqual([]);
      await page.getByRole('navigation', { name: 'Setup steps' }).getByRole('button', { name: /Get it/ }).click();
      await expect(page.getByRole('heading', { level: 2, name: 'Get your configuration' })).toBeVisible();
      // The reader's pick comes first and is marked; the script is one download.
      const pick = page.locator('.cb-wiz-end[data-chosen]');
      await expect(pick.getByRole('heading', { name: 'Apply it yourself' })).toBeVisible();
      const download = page.waitForEvent('download');
      await pick.getByRole('button', { name: 'Download the script' }).click();
      expect((await download).suggestedFilename()).toBe('apply-starterkit-config.sh');
      await expect(page.locator('.cb-wiz-generate')).toContainText('dotnet new starterkit -n MyProduct');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await axe(page)).violations).toEqual([]);
      expect(errors).toEqual([]);
    });
  }
}

test('the guided setup from the home page to a prompt for an AI agent', async ({ page, context }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  // Start empty once; a reload later in the test must keep the answers.
  await page.addInitScript(() => { if (!sessionStorage.getItem('cb-cleared')) { localStorage.removeItem('starterkit-config-builder:v1'); sessionStorage.setItem('cb-cleared', '1'); } });
  await page.goto(`${base}/`);
  await page.getByRole('link', { name: /^Hand it to my AI agent/ }).click();
  await expect(page).toHaveURL(/\/tools\/config-builder\/#guided=agent&step=1$/);
  const guide = page.locator('.cb-wiz');

  // 1. Docker. 2. Leave Search out. 3. RabbitMQ. Every answer is a change the preview shows.
  await guide.getByRole('radio', { name: /^Docker \(Compose\)/ }).check({ force: true });
  await guide.getByRole('button', { name: /^Next: Your product/ }).click();
  await guide.getByRole('button', { name: /^Search: included/ }).click();
  await expect(guide.getByRole('button', { name: /^Search: left out/ })).toHaveAttribute('aria-pressed', 'false');
  await guide.getByRole('button', { name: /^Next: Infrastructure/ }).click();
  await expect(guide.getByRole('heading', { level: 2, name: 'What does it run on?' })).toBeFocused();
  await expect(guide.getByText('Search engine')).toHaveCount(0);
  await guide.locator('#cb-f-Messaging-Transport-row').getByRole('radio', { name: /^In memory/ }).check();
  // Compose sets the transport from .env, so the answer shows up there.
  await expect(guide.locator('.cb-wiz-receipt')).toContainText('MESSAGING_TRANSPORT=InMemory');
  await expect(guide.locator('.cb-wiz-receipt')).toContainText('1 problem would stop startup');
  await expect(guide.getByRole('navigation', { name: 'Setup steps' })).toContainText('1 problem to fix');

  // A reload resumes the same step with the same answers.
  await page.reload();
  await expect(guide.getByRole('heading', { level: 2, name: 'What does it run on?' })).toBeVisible();
  await expect(guide.locator('#cb-f-Messaging-Transport-row').getByRole('radio', { name: /^In memory/ })).toBeChecked();
  // Enter continues from a choice, Back returns.
  await guide.locator('#cb-f-Messaging-Transport-row').getByRole('radio', { name: /^In memory/ }).press('Enter');
  await expect(guide.getByRole('heading', { level: 2, name: 'How is it protected?' })).toBeFocused();
  await guide.getByRole('button', { name: 'Back' }).click();
  await expect(guide.getByRole('heading', { level: 2, name: 'What does it run on?' })).toBeFocused();

  // Review: the blocking check carries its own control, so it is fixed in place.
  await guide.getByRole('navigation', { name: 'Setup steps' }).getByRole('button', { name: /Review/ }).click();
  const issue = guide.locator('.cb-wiz-issue[data-severity="blocking"]').filter({ hasText: 'In-memory messaging' });
  await expect(issue).toBeVisible();
  // Fixing it removes the check, so the control leaves with it: click, not check.
  await issue.getByRole('radio', { name: /^RabbitMQ/ }).click();
  await expect(guide.locator('.cb-wiz-issue[data-severity="blocking"]').filter({ hasText: 'In-memory messaging' })).toHaveCount(0);

  await guide.getByRole('button', { name: 'Finish' }).click();
  await expect(guide.getByRole('heading', { level: 2, name: 'Get your configuration' })).toBeFocused();
  await expect(guide.locator('.cb-wiz-end[data-chosen]').getByRole('heading', { name: 'Send it to your AI agent' })).toBeVisible();
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await guide.getByRole('button', { name: 'Copy the prompt' }).click();
  await expect(guide.getByRole('button', { name: 'Prompt copied' })).toBeVisible();
  const prompt = await page.evaluate(() => navigator.clipboard.readText());
  expect(prompt).toContain('--search false');
  expect(prompt).toContain('src/Api/appsettings.Docker.json');
  expect(prompt).toContain('MESSAGING_TRANSPORT=RabbitMq');
  expect(prompt).toContain('secrets/jwt_secret.txt');
  expect(prompt).not.toMatch(/"(SecretKey|Password|ApiKey)": "(?!CHANGE_ME)[^"]+"/);

  // The full builder shows the same answers.
  await page.getByRole('button', { name: 'Open the full builder' }).first().click();
  await expect(page.getByRole('main', { name: 'Settings' })).toBeVisible();
  await expect(page.getByRole('button', { name: /^Search: left out/ })).toBeVisible();
  await expect(page.locator('.cb-command')).toContainText('--search false');
  await page.getByRole('button', { name: 'Guided setup' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Get your configuration' })).toBeVisible();
});
