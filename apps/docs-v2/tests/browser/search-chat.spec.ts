import { test, expect } from '@playwright/test';
const prefix = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');

test('search filters constrain results and Ask AI opens with clear provider status', async ({ page, request }) => {
  const filtered = await request.get(prefix + '/api/search/?query=authentication&tag=api');
  expect(filtered.ok()).toBe(true);
  const results: { url: string }[] = await filtered.json();
  expect(results.length).toBeGreaterThan(0);
  expect(results.every((result) => result.url.includes('/api-reference/v1/'))).toBe(true);
  await page.goto(prefix + '/architecture/overview/');
  await page.keyboard.press('Control+k');
  await expect(page.getByRole('button', { name: 'All pages', exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Search' }).fill('authentication');
  await page.getByRole('button', { name: 'API reference', exact: true }).click();
  await expect(page.getByRole('option').first()).toBeVisible();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
  const chat = page.getByRole('region', { name: 'Ask AI', exact: true });
  await expect(chat.getByRole('heading', { name: 'How can I help?' })).toBeVisible();
  await chat.getByRole('textbox', { name: 'Your question' }).fill('How do modules communicate?');
  await chat.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(chat.getByRole('alert')).toContainText('not connected');
  await chat.getByRole('button', { name: 'New conversation' }).click();
  await expect(chat.getByRole('heading', { name: 'How can I help?' })).toBeVisible();
});

test('chat displays answers and source links from the server', async ({ page }) => {
  await page.route('**/api/chat/', (route) => route.fulfill({ json: { answer: 'Use ISender and module contracts [1].', sources: [{ title: 'Architecture', url: prefix + '/architecture/overview/' }] } }));
  await page.goto(prefix + '/architecture/overview/');
  await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
  const chat = page.getByRole('region', { name: 'Ask AI', exact: true });
  await chat.getByRole('textbox', { name: 'Your question' }).fill('How do modules communicate?');
  await chat.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(chat.getByRole('log')).toContainText('Use ISender');
  await expect(chat.getByRole('link', { name: 'Architecture', exact: true })).toHaveAttribute('href', prefix + '/architecture/overview/');
});

test('chat renders answer Markdown safely, with citations linked to sources', async ({ page }) => {
  const answer = [
    'Modules talk through **contracts** [1] or integration events [2]. Ignore [9].',
    '',
    '- Send with `ISender`',
    '- Publish with `AddIntegrationEvent`',
    '',
    '```bash',
    'dotnet new starterkit-module -n Orders',
    '```',
    '',
    '| Kind | Delivery |',
    '| --- | --- |',
    '| Integration | Outbox |',
    '',
    '<img src="x" onerror="window.__xss = 1"> [bad](javascript:window.__xss=2) [docs](https://example.com/guide) ![logo](https://example.com/logo.png)',
  ].join('\n');
  await page.route('**/api/chat/', (route) => route.fulfill({ json: { answer, sources: [
    { title: 'Messaging', url: prefix + '/architecture/messaging/' }, { title: 'Overview', url: prefix + '/architecture/overview/' },
  ] } }));
  await page.goto(prefix + '/architecture/overview/');
  await page.getByRole('button', { name: 'Ask AI', exact: true }).click();
  const chat = page.getByRole('region', { name: 'Ask AI', exact: true });
  await chat.getByRole('textbox', { name: 'Your question' }).fill('How do modules communicate?');
  await chat.getByRole('button', { name: 'Send', exact: true }).click();
  const md = chat.locator('.chat-md');
  await expect(md.locator('strong', { hasText: 'contracts' })).toBeVisible();
  await expect(md.locator('ul > li')).toHaveCount(2);
  await expect(md.locator('li code').first()).toHaveText('ISender');
  await expect(md.locator('pre code')).toHaveText('dotnet new starterkit-module -n Orders');
  await expect(md.locator('table th')).toHaveText(['Kind', 'Delivery']);
  await expect(md.getByRole('link', { name: 'Source 1: Messaging' })).toHaveAttribute('href', prefix + '/architecture/messaging/');
  await expect(md.getByRole('link', { name: 'Source 2: Overview' })).toHaveAttribute('href', prefix + '/architecture/overview/');
  await expect(md).toContainText('Ignore [9].'); // no matching source: stays text
  // Raw HTML prints as text, unsafe schemes are not links, images are never fetched.
  await expect(md.locator('img')).toHaveCount(0);
  await expect(md).toContainText('<img src="x"');
  await expect(md.getByText('bad', { exact: true })).toBeVisible();
  await expect(md.getByRole('link', { name: 'bad' })).toHaveCount(0);
  await expect(md.getByRole('link', { name: 'docs' })).toHaveAttribute('rel', 'noopener noreferrer');
  await expect(md.getByRole('link', { name: 'logo' })).toHaveAttribute('href', 'https://example.com/logo.png');
  expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
});
