import { defineConfig, devices } from '@playwright/test'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const configDirectory = path.dirname(fileURLToPath(import.meta.url))
const e2eDataDirectory = path.join(configDirectory, '.e2e-data')
const e2eArtifactsDirectory = path.resolve(configDirectory, '..', '..', '.artifacts', `e2e-${process.pid}`)
const browserChannel = process.env.PLAYWRIGHT_BROWSER_CHANNEL === 'msedge'
  ? 'msedge'
  : process.env.PLAYWRIGHT_BROWSER_CHANNEL === 'chromium'
    ? 'chromium'
    : 'chrome'

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 2 : 0,
  reporter: [['list']],
  outputDir: 'test-results/e2e-artifacts',
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: 'http://127.0.0.1:5174',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      // Use the installed stable Chrome. This avoids a multi-hundred-MB browser
      // download every time a developer clones and runs the one-click smoke test.
      use: { ...devices['Desktop Chrome'], channel: browserChannel },
    },
  ],
  webServer: [
    {
      // Use an isolated artifacts directory so a normal dev Server on 5114
      // cannot lock this run's assemblies while Playwright builds them.
      command: `dotnet run --project ../Server --artifacts-path "${e2eArtifactsDirectory}" --no-launch-profile --urls http://127.0.0.1:5115`,
      url: 'http://127.0.0.1:5115/api/v1/catalog/cards',
      timeout: 120_000,
      reuseExistingServer: false,
      env: {
        // A distinct environment keeps local data-local/dev-overrides out of E2E.
        ASPNETCORE_ENVIRONMENT: 'E2E',
        DataStorage__RootDirectory: e2eDataDirectory,
      },
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --strictPort',
      url: 'http://127.0.0.1:5174',
      timeout: 120_000,
      reuseExistingServer: false,
      env: {
        VITE_DEV_PORT: '5174',
        VITE_API_TARGET: 'http://127.0.0.1:5115',
      },
    },
  ],
})
