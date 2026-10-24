import { useQuery } from '@tanstack/react-query';

import { strategyRunsQuery } from '../api/runsApi';
import { RunsTable } from './RunsTable';

/** The latest runs of one strategy, newest first. */
export function StrategyRuns({ strategyId }: { strategyId: string }) {
  const runs = useQuery(strategyRunsQuery(strategyId));

  if (runs.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить прогоны.
      </p>
    );
  }

  if (!runs.data) {
    return <p role="status">Загрузка прогонов…</p>;
  }

  return runs.data.items.length === 0 ? (
    <p className="text-muted">Прогонов ещё не было.</p>
  ) : (
    <RunsTable runs={runs.data.items} />
  );
}
