import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import { CompareToggle } from '@/modules/comparison';
import { isJobFinished, JobProgress, jobQuery } from '@/modules/jobs';
import { timeframeLabel } from '@/modules/market-data';
import {
  methodLabel,
  OptimizationConfiguration,
  optimizationKeys,
  OptimizationLive,
  OptimizationOverview,
  optimizationQuery,
  OptimizationTrials,
  useOptimizationProgress,
  WalkForwardWindows,
} from '@/modules/optimizations';
import { strategyQuery } from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/optimizations/$optimizationId');

/** An optimization: its job and live trials while it searches, then all trials, windows and finalists. */
export function OptimizationPage() {
  const { optimizationId } = route.useParams();
  const queryClient = useQueryClient();
  const optimization = useQuery(optimizationQuery(optimizationId));
  const job = useQuery(jobQuery(optimizationId));
  const strategy = useQuery({
    ...strategyQuery(optimization.data?.strategyId ?? ''),
    enabled: optimization.data !== undefined,
  });
  const finished = job.data ? isJobFinished(job.data) : false;
  const searching = optimization.data !== undefined && job.data !== undefined && !finished;
  const progress = useOptimizationProgress(optimizationId, searching);

  useEffect(() => {
    if (finished) {
      void queryClient.invalidateQueries({
        queryKey: optimizationKeys.optimization(optimizationId),
      });
      void queryClient.invalidateQueries({ queryKey: optimizationKeys.trials(optimizationId) });
      void queryClient.invalidateQueries({ queryKey: optimizationKeys.windows(optimizationId) });
    }
  }, [finished, queryClient, optimizationId]);

  const back = (
    <nav aria-label="Путь" className="text-sm text-muted">
      <Link to="/strategies" className="hover:text-link">
        Стратегии
      </Link>
      {strategy.data && (
        <>
          {' / '}
          <Link
            to="/strategies/$strategyId"
            params={{ strategyId: strategy.data.id }}
            className="hover:text-link"
          >
            {strategy.data.name}
          </Link>
        </>
      )}
    </nav>
  );

  if (!optimization.data) {
    return (
      <div className="mx-auto max-w-6xl space-y-3">
        {back}
        {optimization.isError ? (
          <p role="alert" className="text-danger-text">
            {optimization.error instanceof ApiError && optimization.error.status === 404
              ? 'Оптимизация не найдена.'
              : 'Не удалось загрузить оптимизацию.'}
          </p>
        ) : (
          <p role="status">Загрузка оптимизации…</p>
        )}
      </div>
    );
  }

  const current = optimization.data;
  const walkForward =
    current.configuration.walkForward !== null && current.configuration.walkForward !== undefined;
  const staged =
    current.configuration.method === 'coarse_to_fine' || current.configuration.method === 'tpe';

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        back={back}
        title={`Оптимизация · ${current.configuration.backtest.symbol} · ${timeframeLabel(current.configuration.backtest.timeframe)}`}
        meta={
          <>
            {methodLabel(current.configuration.method)} · версия {current.strategyVersion} ·
            запущена <DateTime value={current.createdAt} />
          </>
        }
      />
      <div className="grid gap-6 lg:grid-cols-[3fr_1fr]">
        <div className="min-w-0 space-y-6">
          <Card title="Задача">
            <JobProgress jobId={optimizationId} label="Оптимизация" />
          </Card>
          <OptimizationOverview optimization={current} />
          {walkForward && (
            <WalkForwardWindows
              optimizationId={optimizationId}
              summary={current.walkForwardSummary}
            />
          )}
          {searching ? (
            <OptimizationLive progress={progress} showWindow={walkForward} />
          ) : (
            <Card title="Испытания">
              <OptimizationTrials
                optimizationId={optimizationId}
                showWindow={walkForward}
                showStage={staged}
                action={(trial) => (
                  <CompareToggle
                    item={{
                      kind: 'trial',
                      optimizationId,
                      windowIndex: trial.windowIndex,
                      trialIndex: trial.trialIndex,
                    }}
                    label={`Сравнить испытание ${trial.trialIndex + 1}`}
                  />
                )}
              />
            </Card>
          )}
        </div>
        <OptimizationConfiguration optimization={current} />
      </div>
    </div>
  );
}
