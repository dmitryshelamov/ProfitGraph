import type { ActiveJob } from './types';

/** The Worker's lanes in the order the page shows them; each runs one job at a time. */
export const jobLanes = ['main', 'optimization'] as const;

const laneLabels: Record<string, string> = {
  main: 'Прогоны и загрузки',
  optimization: 'Оптимизации',
};

const kindLabels: Record<string, string> = {
  backtest: 'Прогон',
  optimization: 'Оптимизация',
  historical_download: 'Загрузка истории',
};

export function jobLaneLabel(lane: string): string {
  return laneLabels[lane] ?? lane;
}

export function jobKindLabel(kind: string): string {
  return kindLabels[kind] ?? kind;
}

/** How many of the jobs run and how many wait in a queue; paused jobs are neither. */
export function activeJobCounts(jobs: readonly ActiveJob[]): { running: number; queued: number } {
  return {
    running: jobs.filter((job) => job.status === 'Running').length,
    queued: jobs.filter((job) => job.status === 'Queued').length,
  };
}

/** The running job of a lane, its queue in the Worker's order and its paused jobs. */
export function laneJobs(jobs: readonly ActiveJob[], lane: string) {
  const own = jobs.filter((job) => job.lane === lane);

  return {
    running: own.filter((job) => job.status === 'Running'),
    queued: own.filter((job) => job.status === 'Queued'),
    paused: own.filter((job) => job.status === 'Paused'),
  };
}

/** A stable fingerprint of the jobs and their statuses: it changes when a job starts, pauses or leaves the list. */
export function activeJobsFingerprint(jobs: readonly ActiveJob[]): string {
  return jobs.map((job) => `${job.id}:${job.status}`).join(',');
}
