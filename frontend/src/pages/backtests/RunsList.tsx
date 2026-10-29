import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useDeferredValue, useState } from 'react';

import {
  backtestFacetsQuery,
  type RankingFilters,
  rankingQuery,
  type RankingStatus,
  RunsTable,
} from '@/modules/backtests';
import { CompareToggle } from '@/modules/comparison';
import { timeframeLabel, utcDayStart, venuesQuery } from '@/modules/market-data';
import { strategiesQuery } from '@/modules/strategies';
import { isDecimalString } from '@/shared/lib/decimal';
import { Button } from '@/shared/ui/Button';

import {
  ChoiceFilter,
  type FilterOption,
  SearchFilter,
  SelectFilter,
  TextFilter,
} from './FilterControls';
import type { ListSearch, UpdateSearch } from './search';

const fallbackQuote = 'USDT';

const statusLabels: Record<RankingStatus, string> = {
  completed: 'Завершённые',
  failed: 'С ошибкой',
  cancelled: 'Отменённые',
};

const emptyTexts: Record<RankingStatus, string> = {
  completed: 'Завершённых прогонов с такими фильтрами нет.',
  failed: 'Прогонов с ошибкой с такими фильтрами нет.',
  cancelled: 'Отменённых прогонов с такими фильтрами нет.',
};

const originLabels = { manual: 'Вручную', optimization: 'Из оптимизации' } as const;

/** The end of a chosen last day, so the period includes it: the next UTC midnight. */
function utcDayEnd(day: string): string | null {
  const start = utcDayStart(day);

  return start === null ? null : new Date(Date.parse(start) + 86_400_000).toISOString();
}

function percentError(value: string | undefined): string | null {
  return value === undefined || isDecimalString(value) ? null : 'Число, например 5 или -2.5.';
}

/**
 * The user's finished runs with their filters. Completed runs are compared in one quote currency, which starts as the
 * one of the user's most recent run; failed and cancelled runs have no result and are listed by start time. Runs can be
 * picked for a side-by-side comparison.
 */
export function RunsList({ search, update }: { search: ListSearch; update: UpdateSearch }) {
  const strategies = useQuery(strategiesQuery());
  const facets = useQuery(backtestFacetsQuery());
  const venues = useQuery(venuesQuery());
  const [moreOpen, setMoreOpen] = useState(false);
  const status: RankingStatus = search.status ?? 'completed';
  const completed = status === 'completed';
  const quote =
    search.quote ?? latestQuote(strategies.data) ?? facets.data?.quotes[0]?.value ?? fallbackQuote;
  const typedSearch = useDeferredValue(search.q?.trim() ?? '');
  const from = search.from ? utcDayStart(search.from) : null;
  const to = search.to ? utcDayEnd(search.to) : null;
  const filters: RankingFilters = {
    status,
    ...(completed ? { quote } : {}),
    ...(search.strategy ? { strategyId: search.strategy } : {}),
    ...(search.venue ? { venue: search.venue } : {}),
    ...(search.timeframe ? { timeframe: search.timeframe } : {}),
    ...(typedSearch ? { search: typedSearch } : {}),
    ...(search.origin ? { origin: search.origin } : {}),
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
    ...(completed && search.minReturn && !percentError(search.minReturn)
      ? { minReturn: search.minReturn }
      : {}),
    ...(completed && search.maxDrawdown && !percentError(search.maxDrawdown)
      ? { maxDrawdown: search.maxDrawdown }
      : {}),
  };
  const sort = search.sort ?? 'return';
  const order = search.order ?? 'desc';
  const options = rankingQuery(filters, sort, order);
  const ranking = useInfiniteQuery({
    ...options,
    enabled: options.enabled && (strategies.isFetched || search.quote !== undefined),
  });
  const runs = ranking.data?.pages.flatMap((page) => page.items) ?? [];
  const counts = (values: readonly { value: string; count: number }[] | undefined) =>
    new Map(values?.map((item) => [item.value, item.count]));
  const statusCounts = counts(facets.data?.statuses);
  const originCounts = counts(facets.data?.origins);
  const venueNames = new Map(venues.data?.map((venue) => [venue.id, venue.name]));
  const quotes: FilterOption[] = (facets.data?.quotes ?? []).map((item) => ({
    value: item.value,
    label: item.value,
    count: item.count,
  }));
  const extraOpen =
    moreOpen || Boolean(search.from || search.to || search.minReturn || search.maxDrawdown);
  const filtered = Boolean(
    search.q ||
    search.strategy ||
    search.venue ||
    search.timeframe ||
    search.origin ||
    search.from ||
    search.to ||
    search.minReturn ||
    search.maxDrawdown,
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
          options={(facets.data?.strategies ?? []).map((item) => ({
            value: item.id,
            label: item.name,
            count: item.count,
          }))}
          onChange={(strategy) => update({ strategy })}
        />
        <SelectFilter
          label="Площадка"
          value={search.venue}
          options={(facets.data?.venues ?? []).map((item) => ({
            value: item.value,
            label: venueNames.get(item.value) ?? item.value,
            count: item.count,
          }))}
          onChange={(venue) => update({ venue })}
        />
        <SelectFilter
          label="Таймфрейм"
          value={search.timeframe}
          options={(facets.data?.timeframes ?? []).map((item) => ({
            value: item.value,
            label: timeframeLabel(item.value),
            count: item.count,
          }))}
          onChange={(timeframe) => update({ timeframe })}
        />
        <SelectFilter
          label="Источник"
          value={search.origin}
          options={(['manual', 'optimization'] as const).map((value) => ({
            value,
            label: originLabels[value],
            count: originCounts.get(value) ?? 0,
          }))}
          onChange={(origin) => update({ origin: origin as ListSearch['origin'] })}
        />
      </div>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
        <ChoiceFilter
          label="Состояние"
          value={status}
          options={(['completed', 'failed', 'cancelled'] as const).map((value) => ({
            value,
            label: statusLabels[value],
            count: statusCounts.get(value) ?? 0,
          }))}
          onChange={(value) =>
            update({ status: value === 'completed' ? undefined : (value as ListSearch['status']) })
          }
        />
        {completed && (
          <ChoiceFilter
            label="Валюта котировки"
            value={quote}
            options={
              quotes.some((item) => item.value === quote)
                ? quotes
                : [{ value: quote, label: quote }, ...quotes]
            }
            onChange={(value) => update({ quote: value })}
          />
        )}
        <Button
          size="sm"
          variant="ghost"
          aria-expanded={extraOpen}
          onClick={() => setMoreOpen(!extraOpen)}
        >
          {extraOpen ? 'Скрыть период и результат' : 'Период и результат'}
        </Button>
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
                origin: undefined,
                from: undefined,
                to: undefined,
                minReturn: undefined,
                maxDrawdown: undefined,
              })
            }
          >
            Сбросить фильтры
          </Button>
        )}
      </div>
      {extraOpen && (
        <div className="flex flex-wrap items-start gap-3">
          <TextFilter
            label="Запуск с (UTC)"
            type="date"
            value={search.from ?? ''}
            onChange={(value) => update({ from: value || undefined })}
          />
          <TextFilter
            label="Запуск по (UTC)"
            type="date"
            value={search.to ?? ''}
            onChange={(value) => update({ to: value || undefined })}
          />
          {completed && (
            <>
              <TextFilter
                label="Доходность от, %"
                inputMode="decimal"
                value={search.minReturn ?? ''}
                error={percentError(search.minReturn)}
                onChange={(value) => update({ minReturn: value.trim() || undefined })}
              />
              <TextFilter
                label="Просадка до, %"
                inputMode="decimal"
                value={search.maxDrawdown ?? ''}
                error={percentError(search.maxDrawdown)}
                onChange={(value) => update({ maxDrawdown: value.trim() || undefined })}
              />
            </>
          )}
        </div>
      )}
      {ranking.isError ? (
        <p role="alert" className="text-danger-text">
          Не удалось загрузить прогоны.
        </p>
      ) : runs.length === 0 ? (
        <p className="text-muted">{ranking.isPending ? 'Загрузка…' : emptyTexts[status]}</p>
      ) : (
        <>
          <RunsTable
            runs={runs}
            showStrategy
            sorting={
              completed
                ? {
                    sort,
                    order,
                    onSort: (column, direction) => update({ sort: column, order: direction }),
                  }
                : undefined
            }
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
              disabled={ranking.isFetchingNextPage}
              onClick={() => void ranking.fetchNextPage()}
            >
              Показать ещё
            </Button>
          )}
        </>
      )}
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
