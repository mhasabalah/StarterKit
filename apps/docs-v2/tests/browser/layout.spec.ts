import { test, expect } from '@playwright/test';
const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');

test('glass sidebar width, collapse and responsive layout', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(base + '/architecture/overview/');
  await expect(page.locator('#fd-glass-layout')).toBeVisible();
  const sidebar = page.locator('#nd-sidebar');
  expect((await sidebar.boundingBox())!.width).toBeGreaterThanOrEqual(290);
  expect((await sidebar.boundingBox())!.width).toBeLessThanOrEqual(305);
  await page.screenshot({ path: '.generated/glass-desktop.png' });
  await page.getByRole('button', { name: 'Hide Sidebar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Show Sidebar', exact: true }).last()).toBeVisible();
  await page.getByRole('button', { name: 'Show Sidebar', exact: true }).last().click();
  await expect(sidebar).toBeInViewport();
});

test('pages with rich inline content hydrate without errors', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(`${page.url()}: ${error.message}`));
  for (const path of ['/modules/tenancy/', '/modules/notification/', '/subscriptions/billing/', '/testing/load-testing/', '/reference/brand/']) {
    await page.goto(base + path);
    await expect(page.getByRole('button', { name: 'Copy Markdown', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Copy Markdown', exact: true }).focus();
  }
  expect(errors).toEqual([]);
});
