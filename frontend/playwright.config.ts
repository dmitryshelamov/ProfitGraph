import { randomBytes } from 'node:crypto';

import { defineConfig, devices } from '@playwright/test';

// The runner evaluates this file once and workers inherit its environment, so every process shares one run identity.
process.env.PROFITGRAPH_E2E_DATABASE ??= `profitgraph_e2e_${randomBytes(6).toString('hex')}`;
process.env.PROFITGRAPH_E2E_PASSWORD ??= randomBytes(18).toString('base64url');
process.env.PROFITGRAPH_E2E_MASTER_KEY ??= randomBytes(32).toString('base64');

const database = process.env.PROFITGRAPH_E2E_DATABASE;
const apiPort = 5293;
const webPort = 5275;
const bybitPort = 5296;
const shared = [
  `ConnectionStrings__Postgres="Host=localhost;Port=5436;Database=${database};Username=postgres;Password=postgres"`,
  // Tests never reach Bybit: its public API is served by e2e/bybit-stub.ts.
  `Venues__Bybit__BaseAddress=http://127.0.0.1:${bybitPort}/`,
  // Exchange keys in e2e are invented; the master key that encrypts them is generated per run.
  'Secrets__ActiveKeyId=e2e',
  `Secrets__MasterKeys__e2e="$PROFITGRAPH_E2E_MASTER_KEY"`,
];
const api = [
  'ASPNETCORE_ENVIRONMENT=Testing',
  ...shared,
  'Postgres__RunMigrationsOnStartup=true',
  'Auth__BootstrapOwner__Login=e2e-owner',
  `Auth__BootstrapOwner__Password="$PROFITGRAPH_E2E_PASSWORD"`,
  `dotnet run --no-launch-profile --project ../backend/src/ProfitGraph.Api --urls http://127.0.0.1:${apiPort}`,
].join(' ');
// The Worker starts once the API has applied the migrations, and polls quickly so runs finish within a test.
const worker = [
  `until curl -sf http://127.0.0.1:${apiPort}/api/health > /dev/null; do sleep 1; done;`,
  'DOTNET_ENVIRONMENT=Testing',
  ...shared,
  'BackgroundJobs__PollingInterval=00:00:00.200',
  'dotnet run --no-launch-profile --project ../backend/src/ProfitGraph.Worker',
].join(' ');

/**
 * End-to-end tests run the real API and Worker against a disposable database on the test PostgreSQL (5436) with a
 * bootstrap Owner whose password is generated per run, and drop the database afterwards. Bybit is a local stub.
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
      command: `node e2e/bybit-stub.ts ${bybitPort}`,
      url: `http://127.0.0.1:${bybitPort}/health`,
      reuseExistingServer: false,
    },
    {
      command: `node e2e/database.ts create ${database} && ${api}`,
      url: `http://127.0.0.1:${apiPort}/api/health`,
      reuseExistingServer: false,
      timeout: 180_000,
    },
    {
      command: worker,
      wait: { stdout: /Application started/ },
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
