import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 20_000,
  use: { baseURL: 'http://127.0.0.1:4176', trace: 'retain-on-failure' },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: { command: 'npx http-server ./dist/client/browser -p 4176 -c-1', port: 4176, reuseExistingServer: true },
});
