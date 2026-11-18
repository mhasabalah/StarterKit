import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
const inventory: { pages: { newPath: string }[] } = JSON.parse(readFileSync('.generated/inventory.json', 'utf8'));
const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const url = (path: string) => base + path;

test('homepage, onboarding navigation and accessible structure', async ({ page }) => {
  await page.goto(url('/'));
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Your next product');
  await expect(page.getByRole('link', { name: 'Identity & authentication' })).toBeVisible();
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  await page.getByRole('link', { name: 'Start building', exact: false }).last().click();
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
});
test('search returns documentation and keyboard navigation works', async ({ page }) => {
  await page.goto(url('/architecture/overview/'));
  await page.keyboard.press('Control+k');
  const input = page.getByRole('combobox', { name: 'Search' });
  await input.fill('tenant');
  await expect(page.getByRole('option').first()).toBeVisible();
  const before = page.url();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page).not.toHaveURL(before);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.keyboard.press('Control+k');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).not.toBeVisible();
});
test('diagram rendering, source, expansion, theme and download', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(url('/architecture/overview/'));
  const diagram = page.locator('.diagram').first();
  await expect(diagram).toHaveAttribute('data-diagram-state', 'ready');
  await diagram.getByText('View diagram source').click();
  await expect(diagram.locator('pre')).toBeVisible();
  await diagram.locator('pre').focus();
  await expect(diagram.locator('pre')).toBeFocused();
  await diagram.getByRole('button', { name: 'Expand diagram' }).click();
  await expect(page.getByRole('dialog', { name: 'Expanded diagram' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(diagram.locator('.diagram-canvas > svg')).toBeVisible();
  const download = page.waitForEvent('download');
  await diagram.getByRole('button', { name: 'Download diagram SVG' }).click();
  expect((await download).suggestedFilename()).toBe('architecture-diagram.svg');
  const originalSvgId = await diagram.locator('.diagram-canvas > svg').getAttribute('id');
  await page.getByRole('button', { name: 'Dark', exact: true }).click();
  await expect(page.locator('html')).toHaveClass(/dark/);
  await expect(diagram.locator('.diagram-canvas > svg')).not.toHaveAttribute('id', originalSvgId!);
  await expect(diagram).toHaveAttribute('data-diagram-state', 'ready');
  await page.getByRole('button', { name: 'Light', exact: true }).click();
  await expect(page.locator('html')).not.toHaveClass(/dark/);
  expect(errors).toEqual([]);
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
});

test('copy page, highlighted code and real API reference', async ({ page, context, request }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.goto(url('/architecture/overview/'));
  await page.getByRole('button', { name: 'Copy Markdown', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Copied Markdown' })).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('```mermaid');
  await page.goto(url('/getting-started/new-project/'));
  await expect(page.locator('pre code').first()).toBeVisible();
  const schema = await request.get(url('/api-reference/v1/openapi.json'));
  expect(Object.keys((await schema.json()).paths)).toHaveLength(288);
  const sitemap = await (await request.get(url('/sitemap.xml'))).text();
  const operation = sitemap.match(/<loc>([^<]*\/api-reference\/v1\/[^<]+)<\/loc>/)?.[1];
  expect(operation).toBeTruthy();
  const response = await page.goto(new URL(operation!).pathname);
  expect(response?.ok()).toBe(true);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Response Body/ }).first()).toBeVisible();
  await expect(page.locator('#nd-sidebar a[aria-current="page"]')).toHaveCount(1);
  await expect(page.locator('#nd-sidebar a[aria-current="page"]')).toBeInViewport();
});
test('mobile navigation fits and opens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(url('/architecture/overview/'));
  const toggle = page.getByRole('button', { name: /show sidebar|open sidebar|toggle sidebar|menu/i }).first();
  await toggle.click();
  await page.getByRole('button', { name: 'Start here', exact: true }).click();
  await expect(page.getByRole('link', { name: 'New Project', exact: true }).last()).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
test('AI routes, search API, 404 and trailing slash redirect', async ({ request }) => {
  for (const path of ['/llms.txt', '/llms-full.txt', '/llms.mdx/architecture/overview']) {
    const response = await request.get(url(path)); expect(response.ok()).toBe(true); expect(await response.text()).toContain('StarterKit');
  }
  expect((await request.get(url('/llms.mdx/does-not-exist'))).status()).toBe(404);
  expect((await request.get(url('/does-not-exist/'))).status()).toBe(404);
  const search = await request.get(url('/api/search?query=tenant')); expect(search.ok()).toBe(true); expect((await search.json()).length).toBeGreaterThan(0);
  expect((await request.get(url('/architecture/overview'), { maxRedirects: 0 })).status()).toBe(308);
});
test('every migrated page renders and all diagram pages work in production', async ({ page }) => {
  test.setTimeout(600000);
  const errors: string[] = []; page.on('pageerror', (error) => errors.push(`${page.url()}: ${error.message}`));
  for (const entry of inventory.pages) {
    const response = await page.goto(url(entry.newPath)); expect(response?.ok(), entry.newPath).toBe(true);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    for (const diagram of await page.locator('.diagram').all()) await expect(diagram, entry.newPath).toHaveAttribute('data-diagram-state', 'ready');
    expect(await page.locator('img').evaluateAll((images) => (images as HTMLImageElement[]).filter((image) => !image.complete || image.naturalWidth === 0).map((image) => image.src))).toEqual([]);
  }
  expect(errors).toEqual([]);
});
