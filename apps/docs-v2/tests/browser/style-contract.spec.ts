import { test, expect, type Page } from '@playwright/test';

/**
 * The style contract: app/global.css restyles a few Fumadocs internals by DOM shape (the Glass toolbar strip,
 * the sidebar, the labelled outline, the page footer, OpenAPI badges and tabs). A Fumadocs upgrade that renames
 * one of them would not fail layout or accessibility checks; the page would quietly fall back to stock styles.
 * Each test asserts that the selector still matches and that our override is still the computed result.
 */
const base = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const ink = 'rgb(15, 20, 29)';
const paper = 'rgb(255, 255, 255)';

async function open(page: Page, path: string) {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('theme', 'light'));
  await page.goto(base + path);
}
const style = (page: Page, selector: string, property: string, pseudo?: string) =>
  page.locator(selector).first().evaluate((element, [name, pseudoElement]) =>
    getComputedStyle(element, pseudoElement || null).getPropertyValue(name), [property, pseudo] as const);

test('docs shell: toolbar strip, flush sidebar, inverted current row and peer rows', async ({ page }) => {
  await open(page, '/architecture/messaging/');
  const strip = '#fd-glass-layout > div.sticky';
  await expect(page.locator(strip)).toHaveCount(1);
  expect(await style(page, strip, 'border-bottom-width')).toBe('1px');
  expect(await style(page, `${strip} button[data-search-full]`, 'min-width')).toBe('240px');

  expect(await style(page, '#nd-sidebar', 'border-top-left-radius')).toBe('0px');
  expect(await style(page, '#nd-sidebar', 'border-right-width')).toBe('1px');
  const current = '.docs-sidebar a[aria-current="page"]';
  expect(await style(page, current, 'background-color')).toBe(ink);
  expect(await style(page, current, 'content', '::before')).toBe('none');
  const peers = page.locator('.docs-sidebar [id][data-state]:not(button) .mt-4');
  if (await peers.count()) expect(await peers.first().evaluate((element) => getComputedStyle(element).marginTop)).toBe('1px');
});

test('docs page: element tile, labelled outline and ruled previous/next row', async ({ page }) => {
  await open(page, '/architecture/messaging/');
  const tile = page.locator('.page-head .element-tile');
  await expect(tile).toHaveAttribute('data-family', 'practices');
  await expect(tile.locator('.element-tile-symbol')).toHaveText('Ar');

  expect(await style(page, '#nd-toc #nd-toc-title', 'opacity')).toBe('1');
  expect(await style(page, "#nd-toc [style*='grid-template-rows'] a > span", 'white-space')).toBe('normal');

  const footer = "#nd-page .\\@container.grid-cols-2:has(> a .lucide-chevron-left, > a .lucide-chevron-right)";
  await expect(page.locator(footer)).toHaveCount(1);
  expect(await style(page, footer, 'border-top-width')).toBe('2px');
  expect(await style(page, `${footer} > a > p`, 'display')).toBe('none');
  expect(await style(page, `${footer} > a`, 'content', '::before')).toMatch(/Previous|Next/);
});

test('API reference: spec-sheet radii, method chip, inverted status tab and contract tag names', async ({ page }) => {
  await open(page, '/api-reference/v1/two-factor-authentication/EnableTwoFactor/');
  const method = page.locator('#nd-page .font-mono.font-medium.text-blue-600', { hasText: 'POST' }).first();
  await expect(method).toBeVisible();
  expect(await method.evaluate((element) => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');
  expect(await style(page, '#nd-page .rounded-xl', 'border-top-left-radius')).toBe('3px');
  expect(await style(page, '#nd-page .prose div.flex:has(> h2)', 'border-top-width')).toBe('1px');
  const tab = "#nd-page .font-mono > [role='tab'][aria-selected='true']";
  expect(await style(page, tab, 'background-color')).toBe(ink);
  expect(await style(page, tab, 'color')).toBe(paper);
  // Group folders carry the contract's tag names, as plain rows without chips.
  const sidebar = page.locator('.docs-sidebar');
  await expect(sidebar.getByText('Two-Factor Authentication', { exact: true })).toBeVisible();
  await expect(sidebar.getByText('SCIM 2.0', { exact: true })).toHaveCount(1);
});

test('Ask AI answer: user turn, sources with chart chips, status and error states', async ({ page }) => {
  let fail = false;
  await page.route('**/api/chat/', async (route) => {
    if (fail) return route.fulfill({ status: 503, json: { error: 'The assistant is not configured.' } });
    await route.fulfill({ json: { answer: 'Modules talk through contracts or integration events.', sources: [
      { title: 'Messaging', url: '/architecture/messaging/' },
      { title: 'Enable two-factor authentication', url: '/api-reference/v1/two-factor-authentication/EnableTwoFactor/' },
    ] } });
  });
  await open(page, '/architecture/messaging/');
  await page.getByRole('button', { name: /Ask AI/ }).first().click();
  const chat = page.getByRole('region', { name: 'Ask AI' });
  await chat.locator('#docs-ai-question').fill('How do modules communicate?');
  await chat.getByRole('button', { name: 'Send' }).click();
  const sources = chat.getByRole('navigation', { name: 'Answer sources' });
  await expect(sources.getByRole('link')).toHaveCount(2);
  await expect(sources.locator('.element-chip[data-family="practices"]')).toHaveText('Ar');
  await expect(sources.locator('.element-chip[data-family="api"]')).toHaveText('Ap');
  expect(await style(page, '.docs-chat-message[data-role="user"]', 'background-color')).not.toBe('rgba(0, 0, 0, 0)');
  fail = true;
  await chat.locator('#docs-ai-question').fill('And the outbox?');
  await chat.getByRole('button', { name: 'Send' }).click();
  await expect(chat.getByRole('alert')).toHaveText('The assistant is not configured.');
  await expect(chat.getByRole('button', { name: 'Close AI chat' }).locator('svg')).toHaveCount(1);
});

test('opening a home tile morphs it into the page-head tile', async ({ page }) => {
  await open(page, '/');
  const supported = await page.evaluate(() => typeof document.startViewTransition === 'function');
  test.skip(!supported, 'This browser has no View Transitions API');
  await page.locator('.chart').scrollIntoViewIfNeeded();
  await page.waitForLoadState('networkidle'); // the destination must be prefetched for the pair to form
  // React names the pair only while a transition runs, so record the pseudo-elements of the real navigation.
  await page.evaluate(() => {
    const start = document.startViewTransition.bind(document);
    document.startViewTransition = ((update: never) => {
      const transition = start(update);
      transition.ready.then(() => {
        (window as unknown as { pseudo: string[] }).pseudo = document.getAnimations()
          .map((animation) => (animation.effect as KeyframeEffect | null)?.pseudoElement ?? '').filter(Boolean);
      }).catch(() => undefined);
      return transition;
    }) as typeof document.startViewTransition;
  });
  await page.locator('.chart .tile[data-symbol="Id"]').click();
  await page.waitForURL(/\/identity\/$/);
  await expect(page.locator('.page-head .element-tile .element-tile-symbol')).toHaveText('Id');
  const pseudo = await page.waitForFunction(() => (window as unknown as { pseudo?: string[] }).pseudo).then((handle) => handle.jsonValue());
  expect(pseudo).toContain('::view-transition-old(element-Id)');
  expect(pseudo).toContain('::view-transition-new(element-Id)');
  expect(pseudo).not.toContain('::view-transition-old(root)'); // the page swaps at once; only the tile moves
});
