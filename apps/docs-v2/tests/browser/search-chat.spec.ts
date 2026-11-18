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
