import { execFileSync } from 'node:child_process';

/** Drops the run's database; the API is still running, so connections are closed by force. */
export default function globalTeardown() {
  execFileSync('node', ['e2e/database.ts', 'drop', process.env.PROFITGRAPH_E2E_DATABASE!], {
    stdio: 'inherit',
  });
}
