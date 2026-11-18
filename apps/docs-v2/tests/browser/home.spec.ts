import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');

for (const theme of ['light', 'dark']) {
  for (const width of [390, 1440]) {
    test(`documentation homepage in ${theme} at ${width}px`, async ({ page, context }) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await page.setViewportSize({ width, height: 1000 });
      await page.addInitScript((value) => localStorage.setItem('theme', value), theme);
      await page.goto(base + '/');
      const home = page.getByRole('main');
      await expect(home.getByRole('heading', { level: 1 })).toHaveCount(1);
      await expect(page.locator('.brand-logo')).toHaveCount(2);
      await expect(page.locator(`.brand-logo-${theme}:visible`)).toHaveCount(2);
      expect(await page.locator('.brand-logo img:visible').evaluateAll((images) => images.every((image) => (image as HTMLImageElement).complete && (image as HTMLImageElement).naturalWidth > 0))).toBe(true);
      await expect(page.locator('.home-path')).toHaveCount(3);
      await expect(page.locator('.home-guide')).toHaveCount(12);
      await expect(page.getByRole('region', { name: 'Template installation and project creation commands' })).toBeVisible();
      await expect(page.getByRole('group', { name: 'Generated project structure' })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      if (width === 1440 && theme === 'light') {
        await context.grantPermissions(['clipboard-read', 'clipboard-write']);
        const copy = page.locator('.home-command').getByRole('button', { name: 'Copy Text', exact: true });
        await copy.focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('.home-command').getByRole('button', { name: 'Copied Text', exact: true })).toBeVisible();
        expect((await page.evaluate(() => navigator.clipboard.readText())).trim().split(/\r?\n/)).toEqual([
          'dotnet new install StarterKit.Templates', 'dotnet new starterkit -n Acme',
        ]);
      }
      await page.screenshot({ path: `.generated/home-redesign-${theme}-${width}.png`, fullPage: true });
      expect(errors).toEqual([]);
      if (width === 390) {
        await page.setViewportSize({ width: 320, height: 800 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
    });
  }
}
