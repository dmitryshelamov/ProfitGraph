import { Link } from '@tanstack/react-router';

import { Amount } from '@/modules/backtests';
import { jobStatusLabel } from '@/modules/jobs';
import { timeframeLabel } from '@/modules/market-data';
import { DateTime } from '@/shared/ui/DateTime';

import { methodLabel } from '../model/format';
import type { OptimizationSummary } from '../model/types';

/** Optimizations in a list: when, on what, how they search, where they stand and their best objective so far. */
export function OptimizationsTable({
  optimizations,
  showStrategy = false,
}: {
  optimizations: readonly OptimizationSummary[];
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
            <th className="py-2 pr-4 font-normal">Метод</th>
            <th className="py-2 pr-4 font-normal">Состояние</th>
            <th className="py-2 pr-4 font-normal">Испытаний</th>
            <th className="py-2 pr-4 font-normal">Лучшая цель</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {optimizations.map((optimization) => (
            <tr key={optimization.id}>
              <td className="py-2 pr-4">
                <Link
                  to="/optimizations/$optimizationId"
                  params={{ optimizationId: optimization.id }}
                  className="text-link hover:underline"
                >
                  <DateTime value={optimization.createdAt} />
                </Link>
              </td>
              {showStrategy && (
                <td className="py-2 pr-4">
                  {optimization.strategyName}{' '}
                  <span className="text-muted">v{optimization.strategyVersion}</span>
                </td>
              )}
              <td className="py-2 pr-4 font-mono">
                {optimization.symbol} · {timeframeLabel(optimization.timeframe)}
              </td>
              <td className="py-2 pr-4">
                {methodLabel(optimization.method)}
                {optimization.windowCount !== null && (
                  <span className="text-muted" title="Окна walk-forward">
                    {' '}
                    · WF ×{optimization.windowCount}
                  </span>
                )}
              </td>
              <td className="py-2 pr-4">{jobStatusLabel(optimization.status)}</td>
              <td className="py-2 pr-4 font-mono">
                {optimization.executedTrials}
                {optimization.plannedTrials !== null && (
                  <span className="text-muted"> / {optimization.plannedTrials}</span>
                )}
              </td>
              <td className="py-2 pr-4">
                <Amount value={optimization.bestObjective} places={4} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
