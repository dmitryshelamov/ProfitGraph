import { Link } from '@tanstack/react-router';

import { jobStatusLabel } from '@/modules/jobs';
import { timeframeLabel } from '@/modules/market-data';
import { DateTime } from '@/shared/ui/DateTime';

import type { BacktestSummary } from '../model/types';
import { Amount } from './Amount';

/** Runs in a list: when, on what, how they stand and, once completed, their return. */
export function RunsTable({
  runs,
  showStrategy = false,
}: {
  runs: readonly BacktestSummary[];
  showStrategy?: boolean;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-muted">
            <th className="py-2 pr-4 font-normal">Запуск</th>
            {showStrategy && <th className="py-2 pr-4 font-normal">Стратегия</th>}
            <th className="py-2 pr-4 font-normal">Пара</th>
            <th className="py-2 pr-4 font-normal">Таймфрейм</th>
            <th className="py-2 pr-4 font-normal">Состояние</th>
            <th className="py-2 pr-4 font-normal">Доходность, %</th>
            <th className="py-2 pr-4 font-normal">Прибыль</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {runs.map((run) => (
            <tr key={run.id}>
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
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
