import type { BackgroundJob } from './types';

const labels: Record<string, string> = {
  Queued: 'В очереди',
  Running: 'Выполняется',
  Paused: 'Приостановлена',
  Completed: 'Завершена',
  Failed: 'Ошибка',
  Cancelled: 'Отменена',
};

export function jobStatusLabel(status: string): string {
  return labels[status] ?? status;
}

export function isJobFinished(job: Pick<BackgroundJob, 'status'>): boolean {
  return job.status === 'Completed' || job.status === 'Failed' || job.status === 'Cancelled';
}

/** Whole percent of the current stage, or null before the work reports a total. */
export function jobPercent(job: Pick<BackgroundJob, 'completed' | 'total'>): number | null {
  return job.total !== null && job.total > 0
    ? Math.min(100, Math.floor((job.completed * 100) / job.total))
    : null;
}
