import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useDeferredValue } from 'react';

import { timeframeLabel, venuesQuery } from '@/modules/market-data';
import {
  methodLabel,
  type OptimizationFilters,
  type OptimizationListState,
  optimizationsQuery,
  OptimizationsTable,
  searchMethods,
} from '@/modules/optimizations';
import { strategiesQuery } from '@/modules/strategies';
import { Button } from '@/shared/ui/Button';

import { ChoiceFilter, SearchFilter, SelectFilter } from './FilterControls';
import type { ListSearch, UpdateSearch } from './search';

const stateLabels: Record<OptimizationListState | 'all', string> = {
  all: 'Все',
  active: 'В работе',
  completed: 'Завершённые',
  failed: 'С ошибкой',
  cancelled: 'Отменённые',
};

/** The user's optimizations of every strategy, newest first, with their filters. */
export function OptimizationsList({
  search,
  update,
}: {
  search: ListSearch;
  update: UpdateSearch;
}) {
  const strategies = useQuery(strategiesQuery());
  const venues = useQuery(venuesQuery());
  const typedSearch = useDeferredValue(search.q?.trim() ?? '');
  const filters: OptimizationFilters = {
    ...(search.state ? { state: search.state } : {}),
    ...(search.strategy ? { strategyId: search.strategy } : {}),
    ...(search.venue ? { venue: search.venue } : {}),
    ...(search.timeframe ? { timeframe: search.timeframe } : {}),
    ...(typedSearch ? { search: typedSearch } : {}),
    ...(search.method ? { method: search.method } : {}),
  };
  const list = useInfiniteQuery(optimizationsQuery(filters));
  const optimizations = list.data?.pages.flatMap((page) => page.items) ?? [];
  const timeframes = [...new Set(venues.data?.flatMap((venue) => venue.timeframes) ?? [])];
  const filtered = Boolean(
    search.q ||
    search.strategy ||
    search.venue ||
    search.timeframe ||
    search.method ||
    search.state,
  );

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <SearchFilter
          value={search.q ?? ''}
          onChange={(value) => update({ q: value || undefined })}
        />
        <SelectFilter
          label="Стратегия"
          value={search.strategy}
          options={(strategies.data ?? []).map((strategy) => ({
            value: strategy.id,
            label: strategy.name,
          }))}
          onChange={(strategy) => update({ strategy })}
        />
        <SelectFilter
          label="Площадка"
          value={search.venue}
          options={(venues.data ?? []).map((venue) => ({ value: venue.id, label: venue.name }))}
          onChange={(venue) => update({ venue })}
        />
        <SelectFilter
          label="Таймфрейм"
          value={search.timeframe}
          options={timeframes.map((timeframe) => ({
            value: timeframe,
            label: timeframeLabel(timeframe),
          }))}
          onChange={(timeframe) => update({ timeframe })}
        />
        <SelectFilter
          label="Метод"
          value={search.method}
          options={searchMethods.map((method) => ({ value: method, label: methodLabel(method) }))}
          onChange={(method) => update({ method })}
        />
      </div>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
        <ChoiceFilter
          label="Состояние"
          value={search.state ?? 'all'}
          options={(['all', 'active', 'completed', 'failed', 'cancelled'] as const).map(
            (value) => ({
              value,
              label: stateLabels[value],
            }),
          )}
          onChange={(value) =>
            update({ state: value === 'all' ? undefined : (value as OptimizationListState) })
          }
        />
        {filtered && (
          <Button
            size="sm"
            variant="ghost"
            onClick={() =>
              update({
                q: undefined,
                strategy: undefined,
                venue: undefined,
                timeframe: undefined,
                method: undefined,
                state: undefined,
              })
            }
          >
            Сбросить фильтры
          </Button>
        )}
      </div>
      {list.isError ? (
        <p role="alert" className="text-danger-text">
          Не удалось загрузить оптимизации.
        </p>
      ) : optimizations.length === 0 ? (
        <p className="text-muted">
          {list.isPending ? 'Загрузка…' : 'Оптимизаций с такими фильтрами нет.'}
        </p>
      ) : (
        <>
          <OptimizationsTable optimizations={optimizations} showStrategy />
          {list.hasNextPage && (
            <Button
              size="sm"
              disabled={list.isFetchingNextPage}
              onClick={() => void list.fetchNextPage()}
            >
              Показать ещё
            </Button>
          )}
        </>
      )}
    </div>
  );
}
