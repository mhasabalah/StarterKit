import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');

test('sidebar tracks direct links, client navigation and browser history', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(base + '/architecture/overview/');
  const sidebar = page.locator('#nd-sidebar');
  const current = sidebar.locator('a[aria-current="page"]');
  await expect(current).toHaveCount(1);
  await expect(current).toHaveText('Overview');
  await expect(sidebar.getByRole('button', { name: 'Concepts', exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(sidebar.getByRole('button', { name: 'Architecture', exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(sidebar.getByRole('button', { name: 'Operations', exact: true })).toHaveAttribute('aria-expanded', 'false');

  await sidebar.getByRole('link', { name: 'Request lifecycle', exact: true }).click();
  await expect(page).toHaveURL(/\/architecture\/request-lifecycle\/$/);
  await expect(current).toHaveText('Request lifecycle');
  await page.goBack();
  await expect(current).toHaveText('Overview');

  const operations = sidebar.getByRole('button', { name: 'Operations', exact: true });
  await operations.focus();
  await operations.press('Enter');
  await expect(operations).toHaveAttribute('aria-expanded', 'true');
  const content = page.locator(`[id="${await operations.getAttribute('aria-controls')}"]`);
  await expect(content).toHaveCSS('animation-name', 'fd-collapsible-down');
  await operations.press('Space');
  await expect(operations).toHaveAttribute('aria-expanded', 'false');
  await expect(content).toBeHidden();
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await operations.press('Enter');
  await expect(content).toHaveCSS('animation-name', 'none');
});

for (const theme of ['light', 'dark']) {
  for (const width of [390, 1440]) {
    test(`sidebar hierarchy and active page in ${theme} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript((value) => localStorage.setItem('theme', value), theme);
      await page.goto(base + '/architecture/overview/');
      if (width < 768) await page.getByRole('button', { name: 'Open Sidebar', exact: true }).click();
      const sidebar = page.locator('.docs-sidebar:visible');
      const current = sidebar.locator('a[aria-current="page"]');
      await expect(current).toHaveCount(1);
      await expect(current).toBeInViewport();
      const concepts = sidebar.getByRole('button', { name: 'Concepts', exact: true });
      await expect(concepts.locator('svg[aria-hidden="true"]')).toHaveCount(2);
      expect(await current.evaluate((el) => getComputedStyle(el, '::before').width)).toBe('3px');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      const accessibility = await new AxeBuilder({ page }).include('.docs-sidebar').withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
      expect(accessibility.violations).toEqual([]);
      await page.screenshot({ path: `.generated/sidebar-${theme}-${width}.png` });
      if (width < 768) {
        await sidebar.getByRole('link', { name: 'Request lifecycle', exact: true }).click();
        await expect(page).toHaveURL(/\/architecture\/request-lifecycle\/$/);
        await expect(page.getByRole('button', { name: 'Open Sidebar', exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'Open Sidebar', exact: true }).click();
        await expect(page.locator('.docs-sidebar:visible a[aria-current="page"]')).toHaveText('Request lifecycle');
        await page.keyboard.press('Escape');
        await expect(page.getByRole('button', { name: 'Close Sidebar', exact: true })).toBeHidden();
      }
    });
  }
}
