import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useDeferredValue, useState } from 'react';

import { type RankingOrder, rankingQuery, type RankingSort, RunsTable } from '@/modules/backtests';
import { CompareToggle } from '@/modules/comparison';
import { strategiesQuery } from '@/modules/strategies';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';
import { PageHeader } from '@/shared/ui/PageHeader';

const fallbackQuote = 'USDT';

/**
 * The user's completed runs in one quote currency, sorted by a result column (best full-period return first by default);
 * returns in different currencies are not compared. The currency starts as the one of the user's most recent run.
 * Runs can be picked for a side-by-side comparison.
 */
export function BacktestsPage() {
  const strategies = useQuery(strategiesQuery());
  const [typedQuote, setQuote] = useState<string | null>(null);
  const quote = typedQuote ?? latestQuote(strategies.data) ?? fallbackQuote;
  const deferred = useDeferredValue(quote.trim().toUpperCase());
  const [sort, setSort] = useState<RankingSort>('return');
  const [order, setOrder] = useState<RankingOrder>('desc');
  const options = rankingQuery(deferred, sort, order);
  const ranking = useInfiniteQuery({
    ...options,
    enabled: options.enabled && (strategies.isFetched || typedQuote !== null),
  });
  const runs = ranking.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        title="Прогоны"
        meta="Завершённые прогоны; столбцы итогов сортируют список, флажки выбирают прогоны для сравнения."
      />
      <Card>
        <Field label="Валюта котировки" className="mb-4 w-48">
          <Input
            value={quote}
            onChange={(event) => setQuote(event.target.value)}
            spellCheck={false}
          />
        </Field>
        {ranking.isError ? (
          <p role="alert" className="text-danger-text">
            Не удалось загрузить прогоны.
          </p>
        ) : runs.length === 0 ? (
          <p className="text-muted">
            {ranking.isPending ? 'Загрузка…' : 'Завершённых прогонов в этой валюте нет.'}
          </p>
        ) : (
          <>
            <RunsTable
              runs={runs}
              showStrategy
              sorting={{
                sort,
                order,
                onSort: (column, direction) => {
                  setSort(column);
                  setOrder(direction);
                },
              }}
              action={(run) => (
                <CompareToggle
                  item={{ kind: 'run', id: run.id }}
                  label={`Сравнить прогон ${run.symbol}`}
                />
              )}
            />
            {ranking.hasNextPage && (
              <Button
                size="sm"
                className="mt-3"
                disabled={ranking.isFetchingNextPage}
                onClick={() => void ranking.fetchNextPage()}
              >
                Показать ещё
              </Button>
            )}
          </>
        )}
      </Card>
    </div>
  );
}

/** The quote currency of the most recently created run among the strategies' latest runs. */
function latestQuote(
  strategies:
    | ReadonlyArray<{ latestRun: { createdAt: string; quoteAsset: string | null } | null }>
    | undefined,
) {
  return strategies
    ?.flatMap((strategy) => (strategy.latestRun?.quoteAsset ? [strategy.latestRun] : []))
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0]?.quoteAsset;
}
