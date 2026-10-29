import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';

import { jobStatusLabel } from '@/modules/jobs';
import { timeframeLabel } from '@/modules/market-data';
import { DateTime } from '@/shared/ui/DateTime';

import type { RankingOrder, RankingSort } from '../api/runsApi';
import type { BacktestSummary } from '../model/types';
import { Amount } from './Amount';

function SortHeader({
  column,
  label,
  sorting,
}: {
  column: RankingSort;
  label: string;
  sorting?: {
    sort: RankingSort;
    order: RankingOrder;
    onSort: (sort: RankingSort, order: RankingOrder) => void;
  };
}) {
  if (!sorting) {
    return <th className="py-2 pr-4 font-normal">{label}</th>;
  }

  const active = sorting.sort === column;
  const next = active && sorting.order === 'desc' ? 'asc' : 'desc';

  return (
    <th
      className="py-2 pr-4 font-normal"
      aria-sort={active ? (sorting.order === 'asc' ? 'ascending' : 'descending') : 'none'}
    >
      <button
        type="button"
        className="hover:text-link"
        onClick={() => sorting.onSort(column, next)}
      >
        {label}
        {active && (sorting.order === 'asc' ? ' ↑' : ' ↓')}
      </button>
    </th>
  );
}

/**
 * Runs in a list: when, on what, how they stand and, once completed, their result. With `sorting` the result columns
 * sort the list; `action` adds a first column, for example to pick runs to compare.
 */
export function RunsTable({
  runs,
  showStrategy = false,
  sorting,
  action,
}: {
  runs: readonly BacktestSummary[];
  showStrategy?: boolean;
  sorting?: {
    sort: RankingSort;
    order: RankingOrder;
    onSort: (sort: RankingSort, order: RankingOrder) => void;
  };
  action?: (run: BacktestSummary) => ReactNode;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-muted">
            {action && <th className="py-2 pr-4 font-normal" aria-label="Выбор" />}
            <SortHeader column="created" label="Запуск" sorting={sorting} />
            {showStrategy && <th className="py-2 pr-4 font-normal">Стратегия</th>}
            <th className="py-2 pr-4 font-normal">Пара</th>
            <th className="py-2 pr-4 font-normal">Таймфрейм</th>
            <th className="py-2 pr-4 font-normal">Состояние</th>
            <SortHeader column="return" label="Доходность, %" sorting={sorting} />
            <SortHeader column="pnl" label="Прибыль" sorting={sorting} />
            <SortHeader column="drawdown" label="Просадка, %" sorting={sorting} />
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {runs.map((run) => (
            <tr key={run.id}>
              {action && <td className="py-2 pr-4">{action(run)}</td>}
              <td className="py-2 pr-4">
                <Link
                  to="/backtests/$runId"
                  params={{ runId: run.id }}
                  className="text-link hover:underline"
                >
                  <DateTime value={run.createdAt} />
                </Link>
              </td>
              {showStrategy && (
                <td className="py-2 pr-4">
                  {run.strategyName} <span className="text-muted">v{run.strategyVersion}</span>
                  {run.optimizationId && (
                    <Link
                      to="/optimizations/$optimizationId"
                      params={{ optimizationId: run.optimizationId }}
                      className="ml-2 rounded border border-line-strong px-1.5 py-0.5 text-xs text-text-secondary hover:text-text"
                    >
                      из оптимизации
                    </Link>
                  )}
                </td>
              )}
              <td className="py-2 pr-4 font-mono">{run.symbol}</td>
              <td className="py-2 pr-4">{timeframeLabel(run.timeframe)}</td>
              <td className="py-2 pr-4">{jobStatusLabel(run.status)}</td>
              <td className="py-2 pr-4">
                <Amount value={run.returnPercent} />
              </td>
              <td className="py-2 pr-4">
                <Amount value={run.totalPnl} suffix={run.quoteAsset ? ` ${run.quoteAsset}` : ''} />
              </td>
              <td className="py-2 pr-4">
                <Amount value={run.maxDrawdownPercent} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
