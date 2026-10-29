import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { activeJobsQuery } from '../api/jobsApi';
import { activeJobCounts } from '../model/activeJobs';

/**
 * The header's summary of the user's background work: how many jobs run, wait and are paused, linking to the list.
 * Hidden while nothing is active; it warns when jobs wait for a Worker that does not answer.
 */
export function ActiveJobsIndicator() {
  const active = useQuery(activeJobsQuery());
  const jobs = active.data?.jobs ?? [];

  if (jobs.length === 0) {
    return null;
  }

  const { running, queued } = activeJobCounts(jobs);
  const paused = jobs.length - running - queued;
  const stalled = !active.data!.workerOnline && queued > 0;
  const parts = [
    running > 0 && `в работе ${running}`,
    queued > 0 && `в очереди ${queued}`,
    paused > 0 && `на паузе ${paused}`,
  ].filter(Boolean);
  const text = parts.join(' · ');

  return (
    <Link
      to="/backtests"
      className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-sm ${
        stalled
          ? 'border-danger-border bg-danger-bg text-danger-text'
          : 'border-line-strong text-text-secondary hover:text-text'
      }`}
      aria-label={`Фоновые задачи: ${text}${stalled ? ', воркер не отвечает' : ''}`}
    >
      <span
        aria-hidden="true"
        className={`size-2 rounded-full ${stalled ? 'bg-danger-text' : running > 0 ? 'animate-pulse bg-link' : 'bg-muted'}`}
      />
      {text.charAt(0).toUpperCase() + text.slice(1)}
      {stalled && ' · воркер не отвечает'}
    </Link>
  );
}
