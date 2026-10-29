import type { ActiveJob } from './types';

/** Where a job's own page is: a run, an optimization or the history screen of a download. */
export function jobLink(job: Pick<ActiveJob, 'id' | 'kind'>) {
  if (job.kind === 'backtest') {
    return { to: '/backtests/$runId', params: { runId: job.id } } as const;
  }

  if (job.kind === 'optimization') {
    return { to: '/optimizations/$optimizationId', params: { optimizationId: job.id } } as const;
  }

  return { to: '/historical-data' } as const;
}
