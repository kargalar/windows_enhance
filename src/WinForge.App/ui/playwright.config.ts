import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  outputDir: '../../../artifacts/ui-e2e',
  workers: 1,
  timeout: 30000,
  reporter: [['list'], ['json', { outputFile: '../../../artifacts/ui-e2e/results.json' }]],
  use: { baseURL: 'http://127.0.0.1:5178', viewport: { width: 1536, height: 960 }, trace: 'retain-on-failure', screenshot: 'only-on-failure', reducedMotion: 'reduce' },
  webServer: { command: 'npm run preview', url: 'http://127.0.0.1:5178', reuseExistingServer: false },
});
