import { randomBytes } from 'node:crypto';

import { defineConfig, devices } from '@playwright/test';

// The runner evaluates this file once and workers inherit its environment, so every process shares one run identity.
process.env.PROFITGRAPH_E2E_DATABASE ??= `profitgraph_e2e_${randomBytes(6).toString('hex')}`;
process.env.PROFITGRAPH_E2E_PASSWORD ??= randomBytes(18).toString('base64url');

const database = process.env.PROFITGRAPH_E2E_DATABASE;
const apiPort = 5293;
const webPort = 5275;
const api = [
  'ASPNETCORE_ENVIRONMENT=Testing',
  `ConnectionStrings__Postgres="Host=localhost;Port=5436;Database=${database};Username=postgres;Password=postgres"`,
  'Postgres__RunMigrationsOnStartup=true',
  'Auth__BootstrapOwner__Login=e2e-owner',
  `Auth__BootstrapOwner__Password="$PROFITGRAPH_E2E_PASSWORD"`,
  `dotnet run --no-launch-profile --project ../backend/src/ProfitGraph.Api --urls http://127.0.0.1:${apiPort}`,
].join(' ');

/**
 * End-to-end tests run the real API against a disposable database on the test PostgreSQL (5436) with a bootstrap
 * Owner whose password is generated per run, and drop the database afterwards.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    timezoneId: 'UTC',
    baseURL: `http://127.0.0.1:${webPort}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: { ...devices['Desktop Chrome'] } },
    {
      name: 'chromium',
      testIgnore: /auth\.setup\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 1000 },
        storageState: 'test-results/.auth/owner.json',
      },
      dependencies: ['setup'],
    },
  ],
  webServer: [
    {
      command: `node e2e/database.ts create ${database} && ${api}`,
      url: `http://127.0.0.1:${apiPort}/api/health`,
      reuseExistingServer: false,
      timeout: 180_000,
    },
    {
      command: `PROFITGRAPH_API=http://127.0.0.1:${apiPort} npm run dev -- --host 127.0.0.1 --port ${webPort}`,
      url: `http://127.0.0.1:${webPort}`,
      reuseExistingServer: false,
    },
  ],
});
