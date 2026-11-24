import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const url = `${base}/tools/config-builder/`;
const axe = (page: Page) => new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();

async function open(page: Page, width: number, theme = 'light', hash = '') {
  await page.setViewportSize({ width, height: 1000 });
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
  const add = settings.getByRole('textbox', { name: 'Add a provider' });
  await add.fill('openai');
  await add.press('Enter');
  await expect(settings.locator('#cb-f-Agentic-Providers-openai-Endpoint')).toBeVisible();
  await expect(settings.locator('#cb-f-Agentic-Providers-openai-ApiKey')).toContainText('agentic_openai_api_key');
  await add.fill('openai');
  await add.press('Enter');
  await expect(settings.getByRole('alert')).toContainText('A provider named openai exists');
});
