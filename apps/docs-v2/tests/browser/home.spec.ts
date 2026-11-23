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
      await expect(page.locator('#nd-nav .brand-logo')).toHaveCount(1);
      await expect(page.locator('.brand-mark:visible').first()).toBeVisible();
      const chart = page.locator('.chart');
      await expect(chart.locator('.chart-modules .tile')).toHaveCount(11);
      await expect(chart.locator('.chart-practices .tile')).toHaveCount(8);
      await expect(chart.getByRole('link', { name: /^Identity, startup order 000, \d+ pages/ })).toBeVisible();
      await expect(chart.getByRole('link', { name: /^Your module, startup order 700/ })).toBeVisible();
      await expect(page.locator('.home-route')).toHaveCount(3);
      await expect(page.getByRole('button', { name: 'Search modules, rules and commands' })).toBeVisible();
      await expect(page.getByRole('region', { name: 'Template installation and project creation commands' })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      if (width === 1440 && theme === 'light') {
        const foundations = chart.getByRole('button', { name: 'Foundations', exact: true });
        await foundations.click();
        await expect(foundations).toHaveAttribute('aria-pressed', 'true');
        await expect(chart.locator('.tile[data-family="connected"]').first()).toHaveCSS('opacity', '0.6');
        await foundations.click();
        await expect(foundations).toHaveAttribute('aria-pressed', 'false');
        await page.getByRole('button', { name: 'Search modules, rules and commands' }).click();
        await expect(page.getByRole('dialog')).toBeVisible();
        await page.keyboard.press('Escape');
        await context.grantPermissions(['clipboard-read', 'clipboard-write']);
        const copy = page.locator('.home-command').getByRole('button', { name: 'Copy Text', exact: true });
        await copy.focus();
        await page.keyboard.press('Enter');
        await expect(page.locator('.home-command').getByRole('button', { name: 'Copied Text', exact: true })).toBeVisible();
        expect((await page.evaluate(() => navigator.clipboard.readText())).trim().split(/\r?\n/)).toEqual([
          'dotnet new install StarterKit.Templates', 'dotnet new starterkit -n Acme',
        ]);
      }
      await page.screenshot({ path: `.generated/home-chart-${theme}-${width}.png`, fullPage: true });
      expect(errors).toEqual([]);
      if (width === 390) {
        await page.setViewportSize({ width: 320, height: 800 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
    });
  }
}

test('the home chart flags the guide read last', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(base + '/identity/passkeys/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('starterkit:last-read'))).toContain('/identity/passkeys');
  await page.goto(base + '/');
  await expect(page.locator('.tile[data-symbol="Id"][data-last-read]')).toHaveCount(1);
  await expect(page.locator('.continue-reading').getByRole('link')).toHaveAttribute('href', /\/identity\/passkeys/);
});

test('documentation pages carry their chart element beside the title', async ({ page }) => {
  await page.goto(base + '/identity/passkeys/');
  await expect(page.locator('.page-head .element-tile')).toHaveAttribute('aria-label', /^Identity section, startup order 000, \d+ pages$/);
  await page.goto(base + '/decisions/');
  await expect(page.locator('.page-head .element-tile')).toHaveAttribute('aria-label', /^Decisions section, \d+ pages$/);
});

test('the request journey steps through real stations and can be paused', async ({ page }) => {
  test.setTimeout(45000);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(base + '/');
  const journey = page.locator('.journey');
  await journey.scrollIntoViewIfNeeded();
  const stations = journey.locator('.journey-station');
  await expect(stations).toHaveCount(12);
  await page.mouse.move(0, 0);
  await expect(stations.nth(1)).toHaveAttribute('aria-current', 'step', { timeout: 12000 });
  await journey.getByRole('button', { name: 'Pause' }).click();
  await expect(journey.getByRole('button', { name: 'Play' })).toBeVisible();
  await stations.nth(8).click();
  await expect(stations.nth(8)).toHaveAttribute('aria-current', 'step');
  await expect(journey.getByRole('heading', { level: 3 })).toHaveText('Interceptors in one SaveChanges');
  await expect(journey.locator('.journey-card[data-current] .journey-ends li').first()).toContainText('409');
  await journey.getByRole('button', { name: 'Next station' }).click();
  await expect(stations.nth(9)).toHaveAttribute('aria-current', 'step');
});

test('the home says what ships on day one, what it is built on and how it is shaped', async ({ page }) => {
  await page.goto(base + '/');
  const why = page.getByRole('region', { name: 'Start from what every SaaS needs on day one' });
  await expect(why.locator('.why-capability')).toHaveCount(10);
  await expect(why.getByRole('link', { name: /^Identity JWT with rotating refresh tokens/ })).toHaveAttribute('href', /\/identity\/$/);
  await expect(why.locator('.why-stack dt')).toHaveText(['Runtime', 'Data', 'Messaging and jobs', 'Cache and real time', 'Operations', 'Testing']);
  await expect(why.getByRole('link', { name: 'Every package, with its version' })).toHaveAttribute('href', /\/architecture\/technologystack\/$/);
  await expect(why.getByRole('list', { name: 'The architecture in three rules' }).getByRole('listitem')).toHaveCount(3);
});
