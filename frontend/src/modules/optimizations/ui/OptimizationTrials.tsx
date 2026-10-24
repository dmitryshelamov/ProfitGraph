import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/shared/ui/Button';

import { optimizationTrialsQuery } from '../api/optimizationsApi';
import { TrialsTable } from './TrialsTable';

/** All stored trials of a finished optimization, in delivery order, page by page. */
export function OptimizationTrials({
  optimizationId,
  showWindow,
  showStage,
}: {
  optimizationId: string;
  showWindow: boolean;
  showStage: boolean;
}) {
  const trials = useInfiniteQuery(optimizationTrialsQuery(optimizationId));

  if (trials.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить испытания.
      </p>
    );
  }

  if (!trials.data) {
    return <p role="status">Загрузка испытаний…</p>;
  }

  const items = trials.data.pages.flatMap((page) => page.items);

  return items.length === 0 ? (
    <p className="text-muted">Испытаний нет.</p>
  ) : (
    <div className="space-y-3">
      <TrialsTable trials={items} showWindow={showWindow} showStage={showStage} />
      {trials.hasNextPage && (
        <Button
          size="sm"
          disabled={trials.isFetchingNextPage}
          onClick={() => void trials.fetchNextPage()}
        >
          Показать ещё
        </Button>
      )}
    </div>
  );
}
