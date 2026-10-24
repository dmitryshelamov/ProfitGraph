import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { Amount } from '@/modules/backtests';
import { jobStatusLabel } from '@/modules/jobs';
import { timeframeLabel } from '@/modules/market-data';
import { DateTime } from '@/shared/ui/DateTime';

import { strategyOptimizationsQuery } from '../api/optimizationsApi';
import { methodLabel } from '../model/format';

/** The latest optimizations of one strategy, newest first. */
export function StrategyOptimizations({ strategyId }: { strategyId: string }) {
  const optimizations = useQuery(strategyOptimizationsQuery(strategyId));

  if (optimizations.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить оптимизации.
      </p>
    );
  }

  if (!optimizations.data) {
    return <p role="status">Загрузка оптимизаций…</p>;
  }

  return optimizations.data.items.length === 0 ? (
    <p className="text-muted">Оптимизаций ещё не было.</p>
  ) : (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-muted">
            <th className="py-2 pr-4 font-normal">Запуск</th>
            <th className="py-2 pr-4 font-normal">Пара</th>
            <th className="py-2 pr-4 font-normal">Метод</th>
            <th className="py-2 pr-4 font-normal">Состояние</th>
            <th className="py-2 pr-4 font-normal">Испытаний</th>
            <th className="py-2 pr-4 font-normal">Лучшая цель</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {optimizations.data.items.map((optimization) => (
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
              <td className="py-2 pr-4 font-mono">
                {optimization.symbol} · {timeframeLabel(optimization.timeframe)}
              </td>
              <td className="py-2 pr-4">{methodLabel(optimization.method)}</td>
              <td className="py-2 pr-4">{jobStatusLabel(optimization.status)}</td>
              <td className="py-2 pr-4 font-mono">{optimization.executedTrials}</td>
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
