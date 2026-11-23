import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Architecture views (ADR-0035): the LikeC4 model rendered in place, with the Excalidraw hero on System Design.
const prefix = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');

for (const theme of ['light', 'dark'] as const) for (const width of [390, 1440]) {
  test(`architecture views render, explore and stay accessible: ${theme} at ${width}px`, async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    page.on('console', (message) => { if (message.type() === 'error' && !/Failed to load resource/.test(message.text())) errors.push(message.text()); });
    await page.setViewportSize({ width, height: 1000 });
    await page.emulateMedia({ colorScheme: theme });
    await page.goto(prefix + '/architecture/system-design/');

    const hero = page.locator('img[src*=".excalidraw."]');
    await expect(hero).toBeVisible();
    expect(await hero.evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0)).toBe(true);
    await expect(hero).toHaveCSS('filter', theme === 'dark' ? /invert/ : 'none');

    const views = page.locator('.architecture-view');
    expect(await views.count()).toBeGreaterThanOrEqual(5);
    for (const view of await views.all()) {
      await view.scrollIntoViewIfNeeded();
      await expect(view).toHaveAttribute('data-diagram-state', 'ready');
      await expect(view.locator('figcaption strong')).not.toBeEmpty();
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);

    const landscape = page.locator('.architecture-view[data-view-id="index"]');
    await landscape.scrollIntoViewIfNeeded();
    await landscape.getByRole('button', { name: /^Explore / }).click();
    const browser = page.locator('.likec4-view').first();
    await expect.poll(() => browser.evaluate(() => [...document.querySelectorAll('.likec4-view')]
      .some((host) => host.shadowRoot?.querySelector('dialog.likec4-overlay')))).toBe(true);
    await page.keyboard.press('Escape');

    // Scoped to what ADR-0035 adds: the hero and the architecture figures.
    const results = await new AxeBuilder({ page }).include('.architecture-view').include('img[src*=".excalidraw."]').analyze();
    expect(results.violations.map((violation) => `${violation.id}: ${violation.help}`)).toEqual([]);
    expect(errors).toEqual([]);
  });
}

test('a module page shows its system design section and flow', async ({ page }) => {
  await page.goto(prefix + '/attachment/');
  const section = page.locator('#system-design');
  await expect(section).toBeVisible();
  for (const id of ['attachment', 'attachmentUpload']) {
    const view = page.locator(`.architecture-view[data-view-id="${id}"]`);
    await view.scrollIntoViewIfNeeded();
    await expect(view).toHaveAttribute('data-diagram-state', 'ready');
  }
  await expect(page.getByRole('cell', { name: 'ClamAV', exact: true })).toBeVisible();
});

test('agents read architecture views as text outlines', async ({ request }) => {
  const markdown = await (await request.get(prefix + '/llms.mdx/attachment')).text();
  expect(markdown).toContain('**Architecture view: Uploading a file.**');
  expect(markdown).not.toContain('```likec4-view');
});
