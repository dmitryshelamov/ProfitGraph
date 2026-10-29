import { useQuery } from '@tanstack/react-query';

import { strategyOptimizationsQuery } from '../api/optimizationsApi';
import { OptimizationsTable } from './OptimizationsTable';

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
    <OptimizationsTable optimizations={optimizations.data.items} />
  );
}
