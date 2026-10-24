import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';
import { useEffect } from 'react';

import {
  backtestChartQuery,
  backtestKeys,
  backtestRunQuery,
  MetricsTable,
  outcomeLabel,
  RunConfiguration,
  RunFacts,
  RunKpis,
  RunLive,
  useRunProgress,
} from '@/modules/backtests';
import { isJobFinished, JobProgress, jobQuery } from '@/modules/jobs';
import { PriceEquityChart } from '@/modules/market-chart';
import { timeframeLabel } from '@/modules/market-data';
import { strategyQuery } from '@/modules/strategies';
import { ApiError } from '@/shared/api/client';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/backtests/$runId');

function RunChart({ runId }: { runId: string }) {
  const chart = useQuery(backtestChartQuery(runId));

  if (!chart.data) {
    return chart.isError ? (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить график.
      </p>
    ) : (
      <p role="status">Загрузка графика…</p>
    );
  }

  const points = chart.data.points.map((point) => ({ ...point, openTime: point.firstOpenTime }));

  return (
    <Card
      title="График"
      actions={
        chart.data.bucketSize > 1 && (
          <span className="text-sm text-muted">по {chart.data.bucketSize} свечей в точке</span>
        )
      }
    >
      <PriceEquityChart points={points} label="График прогона" />
    </Card>
  );
}

/** A run: its job while it is queued or running with live candles, then its results (stage 3 plan, S3-9). */
export function BacktestRunPage() {
  const { runId } = route.useParams();
  const queryClient = useQueryClient();
  const run = useQuery(backtestRunQuery(runId));
  const job = useQuery(jobQuery(runId));
  const strategy = useQuery({
    ...strategyQuery(run.data?.strategyId ?? ''),
    enabled: run.data !== undefined,
  });
  const finished = job.data ? isJobFinished(job.data) : false;
  const progress = useRunProgress(
    runId,
    run.data !== undefined && job.data !== undefined && !finished,
  );

  useEffect(() => {
    if (finished) {
      void queryClient.invalidateQueries({ queryKey: backtestKeys.run(runId) });
    }
  }, [finished, queryClient, runId]);

  const back = (
    <nav aria-label="Путь" className="text-sm text-muted">
      <Link to="/backtests" className="hover:text-link">
        Прогоны
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

  if (!run.data) {
    return (
      <div className="mx-auto max-w-6xl space-y-3">
        {back}
        {run.isError ? (
          <p role="alert" className="text-danger-text">
            {run.error instanceof ApiError && run.error.status === 404
              ? 'Прогон не найден.'
              : 'Не удалось загрузить прогон.'}
          </p>
        ) : (
          <p role="status">Загрузка прогона…</p>
        )}
      </div>
    );
  }

  const current = run.data;
  const result = current.result;

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        back={back}
        title={`${current.configuration.symbol} · ${timeframeLabel(current.configuration.timeframe)}`}
        meta={
          <>
            Версия {current.strategyVersion} · запущен <DateTime value={current.createdAt} />
            {result && ` · ${outcomeLabel(result.outcome)}`}
          </>
        }
      />
      <div className="grid gap-6 lg:grid-cols-[3fr_1fr]">
        <div className="min-w-0 space-y-6">
          <Card title="Задача">
            <JobProgress jobId={runId} label="Прогон" />
          </Card>
          {result?.failureMessage && (
            <p role="alert" className="text-danger-text">
              {result.failureMessage}
            </p>
          )}
          {!finished && (
            <RunLive
              progress={progress}
              quote={current.history ? ` ${current.history.quoteAsset}` : ''}
            />
          )}
          {finished && result && (
            <>
              <RunKpis run={current} />
              <RunChart runId={runId} />
              <MetricsTable run={current} />
              <RunFacts runId={runId} />
            </>
          )}
        </div>
        <RunConfiguration run={current} />
      </div>
    </div>
  );
}
