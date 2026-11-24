import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const axe = (page: Page) => new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();

async function open(page: Page, path: string, width: number, theme = 'light') {
  await page.setViewportSize({ width, height: width < 768 ? 844 : 900 });
  await page.addInitScript((value) => localStorage.setItem('theme', value), theme);
  await page.goto(base + path, { waitUntil: 'networkidle' }); // hydrated, so taps reach React
}

for (const theme of ['light', 'dark']) {
  test(`phone docs header pins brand, search, Ask AI, GitHub and the menu (${theme})`, async ({ page }) => {
    await open(page, '/architecture/messaging/', 390, theme);
    const bar = page.locator('.docs-header .site-bar');
    await expect(bar).toBeVisible();
    expect((await bar.boundingBox())!.y).toBeLessThanOrEqual(1); // a top bar, not floating bottom buttons
    await expect(bar.getByRole('link', { name: 'StarterKit guide home' })).toHaveAttribute('href', base + '/');
    await expect(bar.getByRole('link', { name: 'StarterKit on GitHub' })).toHaveAttribute('href', /github\.com\//);
    await expect(page.locator('#nd-toc')).toBeHidden(); // the Glass dot rail gives way to the section bar
    expect((await axe(page)).violations).toEqual([]);

    await bar.getByRole('button', { name: 'Search' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.keyboard.press('Escape');
    await bar.getByRole('button', { name: 'Ask AI' }).click();
    await expect(page.getByRole('region', { name: 'Ask AI' })).toBeVisible();
    await page.getByRole('button', { name: 'Close AI chat' }).click();
    const menu = bar.getByRole('button', { name: 'Open navigation menu' });
    await menu.click();
    await expect(menu).toHaveAttribute('aria-expanded', 'true');
    await expect(page.locator('#nd-sidebar-mobile')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

test('the section bar follows the reader and opens to the outline', async ({ page }) => {
  await open(page, '/architecture/messaging/', 390);
  const bar = page.locator('.section-bar');
  const trigger = bar.locator('.section-bar-trigger');
  await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  const heading = page.locator('#nd-page #outbox');
  await heading.scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, 40));
  await expect(bar.locator('.section-bar-label')).toContainText('Outbox');
  expect((await bar.boundingBox())!.y).toBeCloseTo(56, 0); // stuck under the 56px header
  const offset = Number(await bar.locator('.section-ring-value').getAttribute('stroke-dashoffset'));
  expect(offset).toBeLessThan(2 * Math.PI * 7 - 1); // the reading-progress ring has advanced

  await trigger.click();
  const outline = page.getByRole('navigation', { name: 'On this page' });
  await expect(outline).toBeVisible();
  await expect(outline.locator('a[data-active="true"]').first()).toBeVisible();
  expect((await axe(page)).violations).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(outline).toBeHidden();
  await expect(trigger).toBeFocused();

  await trigger.click();
  await outline.getByRole('link', { name: 'Configuration', exact: true }).click();
  await expect(outline).toBeHidden();
  await expect(page).toHaveURL(/#configuration$/);
  await expect(bar.locator('.section-bar-label')).toContainText('Configuration');
});

test('wide screens show the labelled outline with an ink thumb instead of the section bar', async ({ page }) => {
  await open(page, '/architecture/messaging/', 1440);
  await expect(page.locator('.section-bar')).toBeHidden();
  await expect(page.locator('#nd-toc #nd-toc-title')).toBeVisible();
  const active = page.locator("#nd-toc [style*='grid-template-rows'] a[data-active='true']").first();
  await expect(active).toBeVisible();
  expect(await active.evaluate((element) => getComputedStyle(element, '::before').backgroundColor)).toBe('rgb(15, 20, 29)');
  await expect(page.locator('.docs-header').getByRole('link', { name: 'StarterKit on GitHub' })).toBeVisible();
});

for (const width of [390, 1440]) {
  test(`back to top appears after scrolling and returns to the title (${width}px)`, async ({ page }) => {
    await open(page, '/architecture/messaging/', width);
    const button = page.locator('.back-to-top');
    await expect(button).toHaveAttribute('inert');
    await page.evaluate(() => window.scrollTo(0, window.innerHeight * 3));
    await expect(button).toHaveAttribute('data-visible');
    await expect(button).not.toHaveAttribute('inert');
    await button.click();
    await expect.poll(() => page.evaluate(() => window.scrollY)).toBeLessThan(5);
    await expect(page.locator('main h1')).toBeFocused();
    await expect(button).toHaveAttribute('inert');
  });
}

test('home header: links on wide screens, a menu on phones', async ({ page }) => {
  await open(page, '/', 1440);
  const header = page.locator('header#nd-nav');
  await expect(header.getByRole('navigation', { name: 'Guide sections' }).first().getByRole('link')).toHaveCount(4);
  await expect(header.locator('.home-header-menu')).toBeHidden();

  await open(page, '/', 390);
  const menu = header.locator('.home-header-menu');
  await expect(header.locator('.home-header-links')).toBeHidden();
  await menu.click();
  await expect(menu).toHaveAttribute('aria-expanded', 'true');
  const panel = header.locator('.home-menu');
  await expect(panel.getByRole('link', { name: 'Architecture' })).toBeVisible();
  expect((await axe(page)).violations).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();
  await menu.click();
  await panel.getByRole('link', { name: 'Decisions' }).click();
  await expect(page).toHaveURL(/\/decisions\/$/);
});
