import { useInfiniteQuery } from '@tanstack/react-query';
import { useDeferredValue, useState } from 'react';

import { rankingQuery, RunsTable } from '@/modules/backtests';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';
import { PageHeader } from '@/shared/ui/PageHeader';

/** The user's completed runs in one quote currency, best full-period return first; returns in different currencies are not compared. */
export function BacktestsPage() {
  const [quote, setQuote] = useState('USDT');
  const deferred = useDeferredValue(quote.trim().toUpperCase());
  const ranking = useInfiniteQuery(rankingQuery(deferred));
  const runs = ranking.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="mx-auto max-w-6xl">
      <PageHeader
        title="Прогоны"
        meta="Завершённые прогоны, лучшие по доходности за весь период — сверху."
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
            <RunsTable runs={runs} showStrategy />
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
