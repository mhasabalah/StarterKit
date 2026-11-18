import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { diagramPalettes } from '../../lib/mermaid-theme.mjs';

const prefix = (process.env.NEXT_PUBLIC_DOCS_BASE || '').replace(/\/$/, '');
const rgb = (hex: string) => `rgb(${hex.slice(1).match(/../g)!.map((value) => parseInt(value, 16)).join(', ')})`;

for (const theme of ['light', 'dark'] as const) for (const width of [390, 1440]) {
  test(`semantic diagrams, readable scrolling and export: ${theme} at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.emulateMedia({ colorScheme: theme });
    await page.goto(prefix + '/architecture/overview/');
    const diagram = page.locator('.diagram').first();
    await expect(diagram).toHaveAttribute('data-diagram-state', 'ready');
    await expect(diagram.locator('.node.api rect').first()).toHaveCSS('fill', rgb(diagramPalettes[theme].roles.api[0]));
    await expect(diagram.locator('.node.application rect').first()).toHaveCSS('fill', rgb(diagramPalettes[theme].roles.application[0]));
    await expect(diagram.locator('svg > title')).not.toBeEmpty();
    await expect(diagram.locator('svg > desc')).not.toBeEmpty();
    const canvas = diagram.getByRole('region');
    if (width === 390) {
      await canvas.focus();
      await page.keyboard.press('ArrowRight');
      await expect.poll(() => canvas.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
      await expect(diagram.getByText('Scroll to explore the diagram, or use Expand.')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    const download = page.waitForEvent('download');
    await diagram.getByRole('button', { name: 'Download diagram SVG' }).click();
    const file = await (await download).path();
    expect(await readFile(file!, 'utf8')).toContain(diagramPalettes[theme].roles.application[1]);
    await diagram.getByRole('button', { name: 'Expand diagram' }).click();
    await expect(page.getByRole('dialog', { name: 'Expanded diagram' })).toBeVisible();
    const ids = await page.locator('svg[id]').evaluateAll((elements) => elements.map((element) => element.id));
    expect(new Set(ids).size).toBe(ids.length);
    await page.keyboard.press('Escape');
    await expect(diagram.locator('.diagram-canvas > svg')).toBeVisible();
    await diagram.screenshot({ path: `.generated/diagram-production-${theme}-${width}.png` });
  });
}
