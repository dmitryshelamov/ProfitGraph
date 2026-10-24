import { useQueries } from '@tanstack/react-query';
import { getRouteApi, Link } from '@tanstack/react-router';

import { Amount, backtestChartQuery, backtestRunQuery } from '@/modules/backtests';
import {
  type ComparisonColumn,
  type ComparisonItem,
  ComparisonTable,
  itemKey,
  parseComparison,
} from '@/modules/comparison';
import { EquityComparisonChart, type EquityLine } from '@/modules/market-chart';
import { timeframeLabel } from '@/modules/market-data';
import { optimizationQuery, optimizationTrialQuery } from '@/modules/optimizations';
import { parameterReadableName } from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';
import { PageHeader } from '@/shared/ui/PageHeader';

const route = getRouteApi('/_authenticated/_shell/compare');

type RunItem = Extract<ComparisonItem, { kind: 'run' }>;
type TrialItem = Extract<ComparisonItem, { kind: 'trial' }>;

function format(value: string | number | null | undefined) {
  return typeof value === 'number' ? (
    <span className="font-mono">{value}</span>
  ) : (
    <Amount value={value} />
  );
}

/**
 * Runs and optimization trials side by side (stage 4 plan, S4-11): their parameter values, their metrics for the whole
 * period, the in-sample part and the holdout, and the runs' equity on one chart. The address holds the comparison.
 */
export function ComparePage() {
  const items = parseComparison(route.useSearch());
  const runs = items.filter((item): item is RunItem => item.kind === 'run');
  const trials = items.filter((item): item is TrialItem => item.kind === 'trial');
  const runResults = useQueries({ queries: runs.map((item) => backtestRunQuery(item.id)) });
  const charts = useQueries({ queries: runs.map((item) => backtestChartQuery(item.id)) });
  const trialResults = useQueries({
    queries: trials.map((item) =>
      optimizationTrialQuery(item.optimizationId, item.windowIndex, item.trialIndex),
    ),
  });
  const optimizations = useQueries({
    queries: trials.map((item) => optimizationQuery(item.optimizationId)),
  });
  const failed = [...runResults, ...trialResults, ...optimizations].some(
    (result) => result.isError,
  );
  const loading = [...runResults, ...trialResults, ...optimizations].some(
    (result) => result.isPending,
  );

  const columns: ComparisonColumn[] = [
    ...runs.flatMap((item, index): ComparisonColumn[] => {
      const run = runResults[index]?.data;

      if (!run) {
        return [];
      }

      const metrics = run.result?.metrics;

      return [
        {
          key: itemKey(item),
          title: (
            <Link
              to="/backtests/$runId"
              params={{ runId: run.id }}
              className="text-link hover:underline"
            >
              Прогон {run.configuration.symbol} · {timeframeLabel(run.configuration.timeframe)}
            </Link>
          ),
          subtitle: `версия ${run.strategyVersion}`,
          parameters: run.configuration.parameters,
          metrics: {
            fullReturn: metrics?.full.returnPercent ?? null,
            fullPnl: metrics?.full.totalPnl ?? null,
            fullDrawdown: metrics?.full.maxDrawdownPercent ?? null,
            fullExecutions: metrics?.full.executions ?? null,
            inSampleReturn: metrics?.inSample.returnPercent ?? null,
            inSamplePnl: metrics?.inSample.totalPnl ?? null,
            inSampleDrawdown: metrics?.inSample.maxDrawdownPercent ?? null,
            inSampleExecutions: metrics?.inSample.executions ?? null,
            outOfSampleReturn: metrics?.outOfSample?.returnPercent ?? null,
            outOfSampleDrawdown: metrics?.outOfSample?.maxDrawdownPercent ?? null,
          },
        },
      ];
    }),
    ...trials.flatMap((item, index): ComparisonColumn[] => {
      const trial = trialResults[index]?.data;
      const optimization = optimizations[index]?.data;

      if (!trial || !optimization) {
        return [];
      }

      const backtest = optimization.configuration.backtest;
      const walkForward =
        optimization.configuration.walkForward !== null &&
        optimization.configuration.walkForward !== undefined;

      return [
        {
          key: itemKey(item),
          title: (
            <Link
              to="/optimizations/$optimizationId"
              params={{ optimizationId: optimization.id }}
              className="text-link hover:underline"
            >
              Испытание №{trial.trialIndex + 1}
              {walkForward && `, окно ${trial.windowIndex + 1}`}
            </Link>
          ),
          subtitle: `оптимизация ${backtest.symbol} · ${timeframeLabel(backtest.timeframe)}`,
          parameters: { ...backtest.parameters, ...trial.parameters },
          metrics: {
            inSampleReturn: trial.returnPercent,
            inSamplePnl: trial.totalPnl,
            inSampleDrawdown: trial.maxDrawdownPercent,
            inSampleExecutions: trial.executions,
            objective: trial.objective,
          },
        },
      ];
    }),
  ];
  const lines: EquityLine[] = runs.flatMap((_, index): EquityLine[] => {
    const run = runResults[index]?.data;
    const chart = charts[index]?.data;

    return run && chart
      ? [
          {
            label: `${run.configuration.symbol} v${run.strategyVersion}`,
            points: chart.points.map((point) => ({
              time: point.firstOpenTime,
              equity: point.equity,
            })),
          },
        ]
      : [];
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <PageHeader
        title="Сравнение"
        meta="Прогоны и испытания оптимизаций рядом: параметры и метрики по частям периода."
      />
      {items.length === 0 ? (
        <p className="text-muted">
          Отметьте прогоны в списке прогонов или испытания на странице оптимизации и откройте
          сравнение.
        </p>
      ) : (
        <>
          {failed && (
            <p role="alert" className="text-danger-text">
              Часть выбранного не удалось загрузить.
            </p>
          )}
          {loading && <p role="status">Загрузка…</p>}
          <Card title="Параметры и метрики">
            <ComparisonTable
              columns={columns}
              readableName={parameterReadableName}
              format={format}
            />
          </Card>
          {lines.length > 0 && (
            <Card title="Капитал прогонов">
              <EquityComparisonChart lines={lines} label="Капитал сравниваемых прогонов" />
            </Card>
          )}
        </>
      )}
    </div>
  );
}
