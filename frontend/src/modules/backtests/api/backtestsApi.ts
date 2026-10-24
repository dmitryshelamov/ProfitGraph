import { api, type Schemas, unwrap } from '@/shared/api/client';

import type { StartBacktestRequest } from '../model/backtestSetup';

/** Freezes a run of a saved strategy version and queues it; the run has the job's identifier. */
export async function startBacktest(
  request: StartBacktestRequest,
): Promise<Schemas['BackgroundJobResponse']> {
  return unwrap(await api.POST('/api/backtests', { body: request }));
}
