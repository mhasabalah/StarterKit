import { defineConfig, devices } from '@playwright/test';
export default defineConfig({
  testDir: './tests/browser', fullyParallel: false, workers: 1, retries: process.env.CI ? 1 : 0,
  timeout: 60000, expect: { timeout: 15000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: process.env.DOCS_TEST_URL || 'http://localhost:3000', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: process.env.PLAYWRIGHT_CHANNEL } }],
  webServer: process.env.DOCS_TEST_URL ? undefined : { command: 'npm run start', url: `http://localhost:3000${process.env.NEXT_PUBLIC_DOCS_BASE || ''}/`, reuseExistingServer: !process.env.CI, timeout: 60000 },
});
