import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { jobKeys } from '@/modules/jobs';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';

import { optimizationKeys, pauseOptimization, resumeOptimization } from '../api/optimizationsApi';
import { stageLabel } from '../model/format';
import type { Optimization } from '../model/types';

/**
 * Where the search stands: stage, trials by outcome against the plan, the best trial and the finalist runs, with
 * pause and resume while the job allows them.
 */
export function OptimizationOverview({ optimization }: { optimization: Optimization }) {
  const queryClient = useQueryClient();
  const refresh = () => {
    void queryClient.invalidateQueries({
      queryKey: optimizationKeys.optimization(optimization.id),
    });
    void queryClient.invalidateQueries({ queryKey: jobKeys.detail(optimization.id) });
  };
  const pause = useMutation({
    mutationFn: () => pauseOptimization(optimization.id),
    onSuccess: refresh,
  });
  const resume = useMutation({
    mutationFn: () => resumeOptimization(optimization.id),
    onSuccess: refresh,
  });
  const counters = optimization.counters;
  const executed = counters.completed + counters.rejectedByConstraint + counters.failed;
  const canPause = optimization.status === 'Queued' || optimization.status === 'Running';

  return (
    <Card
      title="Поиск"
      actions={
        <>
          {canPause && (
            <Button size="sm" disabled={pause.isPending} onClick={() => pause.mutate()}>
              Приостановить
            </Button>
          )}
          {optimization.status === 'Paused' && (
            <Button size="sm" disabled={resume.isPending} onClick={() => resume.mutate()}>
              Продолжить
            </Button>
          )}
        </>
      }
    >
      <dl className="grid gap-3 text-sm sm:grid-cols-3">
        <div>
          <dt className="text-muted">Этап</dt>
          <dd>{stageLabel(optimization.stage)}</dd>
        </div>
        <div>
          <dt className="text-muted">Выполнено испытаний</dt>
          <dd className="font-mono">
            {executed}
            {optimization.plannedTrials !== null &&
              optimization.plannedTrials !== undefined &&
              ` из ${optimization.plannedTrials}`}
          </dd>
        </div>
        <div>
          <dt className="text-muted">По итогу</dt>
          <dd>
            успешно {counters.completed}, вне ограничений {counters.rejectedByConstraint}, ошибок{' '}
            {counters.failed}, отклонено шаблоном {counters.rejectedByTemplate}
          </dd>
        </div>
        {optimization.best && (
          <div>
            <dt className="text-muted">Лучшее испытание</dt>
            <dd className="font-mono">
              №{optimization.best.trialIndex + 1}, цель {optimization.best.objective}
            </dd>
          </div>
        )}
        {optimization.finalists.length > 0 && (
          <div className="sm:col-span-2">
            <dt className="text-muted">Финалисты — полные прогоны</dt>
            <dd className="flex flex-wrap gap-3">
              {optimization.finalists.map((runId, index) => (
                <Link
                  key={runId}
                  to="/backtests/$runId"
                  params={{ runId }}
                  className="text-link hover:underline"
                >
                  Финалист {index + 1}
                </Link>
              ))}
            </dd>
          </div>
        )}
      </dl>
      {pause.error && <ApiErrorMessage error={pause.error} fallback="Не удалось приостановить." />}
      {resume.error && <ApiErrorMessage error={resume.error} fallback="Не удалось продолжить." />}
    </Card>
  );
}
