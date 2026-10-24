import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ApiError } from '@/shared/api/client';
import { Button } from '@/shared/ui/Button';
import { StatusBadge, type StatusTone } from '@/shared/ui/StatusBadge';

import { cancelJob, jobKeys, jobQuery } from '../api/jobsApi';
import { isJobFinished, jobPercent, jobStatusLabel } from '../model/jobStatus';

function tone(status: string): StatusTone {
  if (status === 'Completed') {
    return 'success';
  }

  return status === 'Failed' ? 'danger' : 'neutral';
}

/**
 * The status and progress of a background job, with cancellation while it is not finished; a completed job's result
 * is shown by `describeResult`.
 */
export function JobProgress({
  jobId,
  label,
  describeResult,
}: {
  jobId: string;
  label: string;
  describeResult?: (result: unknown) => ReactNode;
}) {
  const queryClient = useQueryClient();
  const job = useQuery(jobQuery(jobId));
  const cancel = useMutation({
    mutationFn: () => cancelJob(jobId),
    onSuccess: (updated) => queryClient.setQueryData(jobKeys.detail(jobId), updated),
  });

  if (!job.data) {
    return job.isError ? (
      <p role="alert" className="text-danger-text">
        Не удалось получить состояние задачи.
      </p>
    ) : (
      <p role="status">Загрузка состояния задачи…</p>
    );
  }

  const current = job.data;
  const percent = jobPercent(current);
  const cancelFailure =
    cancel.error instanceof ApiError && cancel.error.code !== 'job_finished'
      ? cancel.error.message
      : null;

  return (
    <div className="space-y-3" aria-label={label} role="group">
      <div className="flex flex-wrap items-center gap-3">
        <StatusBadge tone={tone(current.status)}>{jobStatusLabel(current.status)}</StatusBadge>
        {percent !== null && !isJobFinished(current) && (
          <span className="text-sm text-text-secondary">
            {current.completed} из {current.total}
          </span>
        )}
        {!isJobFinished(current) && (
          <Button
            size="sm"
            className="ml-auto"
            disabled={current.cancelRequested || cancel.isPending}
            onClick={() => cancel.mutate()}
          >
            {current.cancelRequested ? 'Отмена запрошена' : 'Отменить'}
          </Button>
        )}
      </div>
      {percent !== null && !isJobFinished(current) && (
        <progress
          className="h-2 w-full"
          max={100}
          value={percent}
          aria-label={`${label}: ${percent}%`}
        />
      )}
      {current.status === 'Completed' && describeResult && (
        <p className="text-sm">{describeResult(current.result)}</p>
      )}
      {current.failure && (
        <p role="alert" className="text-sm text-danger-text">
          {current.failure.message}
        </p>
      )}
      {cancelFailure && (
        <p role="alert" className="text-sm text-danger-text">
          {cancelFailure}
        </p>
      )}
    </div>
  );
}
