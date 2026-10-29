import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { timeframeLabel } from '@/modules/market-data';
import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import { activeJobsQuery, cancelJob, jobKeys } from '../api/jobsApi';
import { jobKindLabel, jobLaneLabel, jobLanes, laneJobs } from '../model/activeJobs';
import { jobLink } from '../model/jobLink';
import { jobPercent } from '../model/jobStatus';
import type { ActiveJob } from '../model/types';

function JobTitle({ job }: { job: ActiveJob }) {
  const series = [job.symbol, job.timeframe && timeframeLabel(job.timeframe)]
    .filter(Boolean)
    .join(' · ');

  return (
    <span className="min-w-0">
      <Link {...jobLink(job)} className="text-link hover:underline">
        {jobKindLabel(job.kind)}
      </Link>{' '}
      {job.strategyName && (
        <>
          {job.strategyName} <span className="text-muted">v{job.strategyVersion}</span>
        </>
      )}
      {series && <span className="font-mono"> {series}</span>}
      {job.optimizationId && <span className="text-muted"> · финалист оптимизации</span>}
    </span>
  );
}

function CancelButton({ job }: { job: ActiveJob }) {
  const queryClient = useQueryClient();
  const cancel = useMutation({
    mutationFn: () => cancelJob(job.id),
    onSettled: () => queryClient.invalidateQueries({ queryKey: jobKeys.active }),
  });

  if (job.cancelRequested || cancel.isSuccess) {
    return <span className="text-sm text-muted">Отмена запрошена</span>;
  }

  return (
    <span className="flex items-center gap-2">
      {cancel.error instanceof ApiError && cancel.error.code !== 'job_finished' && (
        <span role="alert" className="text-sm text-danger-text">
          {cancel.error.message}
        </span>
      )}
      <Button
        size="sm"
        variant="ghost"
        disabled={cancel.isPending}
        onClick={() => cancel.mutate()}
        aria-label={`Отменить: ${jobKindLabel(job.kind)} ${job.symbol ?? ''}`.trim()}
      >
        Отменить
      </Button>
    </span>
  );
}

function RunningJob({ job }: { job: ActiveJob }) {
  const percent = jobPercent(job);

  return (
    <div className="space-y-2">
      <div className="flex items-start justify-between gap-3 text-sm">
        <JobTitle job={job} />
        <CancelButton job={job} />
      </div>
      <div
        role="progressbar"
        aria-label={`Ход: ${jobKindLabel(job.kind)}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent ?? undefined}
        className="h-1.5 overflow-hidden rounded-full bg-panel-2"
      >
        <div className="h-full bg-link transition-[width]" style={{ width: `${percent ?? 0}%` }} />
      </div>
      <p className="text-xs text-muted">
        {percent === null ? 'Подготовка' : `${percent}%`}
        {job.total !== null && job.kind === 'optimization' && ` · ${job.completed} из ${job.total}`}
        {job.startedAt && (
          <>
            {' · '}начата <DateTime value={job.startedAt} />
          </>
        )}
      </p>
    </div>
  );
}

function Lane({ lane, jobs }: { lane: string; jobs: readonly ActiveJob[] }) {
  const { running, queued, paused } = laneJobs(jobs, lane);
  const label = jobLaneLabel(lane);

  return (
    <section aria-label={label} className="space-y-3 rounded-md border border-line p-4">
      <header className="flex items-baseline justify-between gap-3 text-sm">
        <h3 className="font-medium">{label}</h3>
        <span className="text-muted">
          {queued.length > 0 ? `в очереди ${queued.length}` : 'очередь пуста'}
        </span>
      </header>
      {running.length === 0 ? (
        <p className="text-sm text-muted">Ваших задач сейчас не выполняется.</p>
      ) : (
        running.map((job) => <RunningJob key={job.id} job={job} />)
      )}
      {queued.length > 0 && (
        <ol
          aria-label={`Очередь: ${label}`}
          className="divide-y divide-line border-t border-line text-sm"
        >
          {queued.map((job) => (
            <li key={job.id} className="flex items-start justify-between gap-3 py-2">
              <span className="flex min-w-0 gap-2">
                <span
                  className="shrink-0 font-mono text-muted"
                  title="Место среди всех задач этой очереди"
                >
                  №{job.queuePosition}
                </span>
                <JobTitle job={job} />
              </span>
              <CancelButton job={job} />
            </li>
          ))}
        </ol>
      )}
      {paused.length > 0 && (
        <ul
          aria-label={`Приостановлены: ${label}`}
          className="divide-y divide-line border-t border-line text-sm"
        >
          {paused.map((job) => (
            <li key={job.id} className="flex items-start justify-between gap-3 py-2">
              <JobTitle job={job} />
              <span className="shrink-0 text-warning-text">на паузе</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * The user's background work lane by lane: the Worker runs one job at a time in each, so a queued job shows its place
 * among every user's queued jobs of the lane. Queued and running jobs can be cancelled; a paused optimization resumes
 * on its own page.
 */
export function ActiveJobsPanel() {
  const active = useQuery(activeJobsQuery());

  if (active.isError) {
    return (
      <Card title="В работе">
        <p role="alert" className="text-danger-text">
          Не удалось загрузить фоновые задачи.
        </p>
      </Card>
    );
  }

  if (!active.data) {
    return (
      <Card title="В работе">
        <p role="status" className="text-muted">
          Загрузка…
        </p>
      </Card>
    );
  }

  const { jobs, workerOnline } = active.data;

  return (
    <Card title="В работе">
      <p className="mb-3 text-sm text-muted">Воркер ведёт по одной задаче в каждой очереди.</p>
      {!workerOnline && jobs.some((job) => job.status === 'Queued') && (
        <p
          role="alert"
          className="mb-3 rounded-md border border-danger-border bg-danger-bg px-3 py-2 text-sm text-danger-text"
        >
          Воркер не отвечает: задачи ждут в очереди и не начнутся, пока он не запустится.
        </p>
      )}
      {jobs.length === 0 ? (
        <p className="text-sm text-muted">Сейчас ничего не выполняется и не ждёт в очереди.</p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {jobLanes.map((lane) => (
            <Lane key={lane} lane={lane} jobs={jobs} />
          ))}
        </div>
      )}
    </Card>
  );
}
