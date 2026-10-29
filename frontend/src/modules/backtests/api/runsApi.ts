import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api, ApiError, unwrap } from '@/shared/api/client';

export const backtestKeys = {
  run: (id: string) => ['backtests', id] as const,
  chart: (id: string) => ['backtests', id, 'chart'] as const,
  facts: (id: string, kind: FactKind) => ['backtests', id, kind] as const,
  strategyRuns: (strategyId: string) => ['strategies', strategyId, 'backtests'] as const,
  rankings: ['backtests', 'ranking'] as const,
  ranking: (filters: RankingFilters, sort: RankingSort, order: RankingOrder) =>
    ['backtests', 'ranking', filters, sort, order] as const,
  facets: ['backtests', 'facets'] as const,
};

export type FactKind = 'orders' | 'fills' | 'diagnostics';

/** The ranking's column and direction; ties fall back to the start time in the same direction. */
export type RankingSort = 'return' | 'pnl' | 'drawdown' | 'created';
export type RankingOrder = 'asc' | 'desc';

/** How listed runs ended; failed and cancelled runs have no result and are ordered by start time. */
export type RankingStatus = 'completed' | 'failed' | 'cancelled';

/** Who started a run: the user or an optimization confirming its finalists. */
export type RankingOrigin = 'manual' | 'optimization';

/**
 * The run list's filters; an absent value filters nothing. Completed runs need a quote currency; `from` and `to` are
 * UTC instants bounding the start time, `minReturn` and `maxDrawdown` decimal strings in percent.
 */
export interface RankingFilters {
  status: RankingStatus;
  quote?: string;
  strategyId?: string;
  venue?: string;
  timeframe?: string;
  search?: string;
  origin?: RankingOrigin;
  from?: string;
  to?: string;
  minReturn?: string;
  maxDrawdown?: string;
}

/** Another user's run is a 404 like a missing one; retrying cannot change that. */
function retryUnlessMissing(count: number, error: Error): boolean {
  return !(error instanceof ApiError && error.status === 404) && count < 2;
}

export function backtestRunQuery(id: string) {
  return queryOptions({
    queryKey: backtestKeys.run(id),
    queryFn: async () => unwrap(await api.GET('/api/backtests/{id}', { params: { path: { id } } })),
    retry: retryUnlessMissing,
  });
}

export function backtestChartQuery(id: string) {
  return queryOptions({
    queryKey: backtestKeys.chart(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/backtests/{id}/chart', { params: { path: { id } } })),
    retry: retryUnlessMissing,
  });
}

/** Candle observations after a sequence; a client fills gaps in progress messages with it. */
export async function backtestObservations(id: string, after: number, limit = 100) {
  return unwrap(
    await api.GET('/api/backtests/{id}/observations', {
      params: { path: { id }, query: { after, limit } },
    }),
  );
}

/** Stored facts of a run, page by page in their stable order; each page names the cursor of the next one. */
export function backtestOrdersQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: backtestKeys.facts(id, 'orders'),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/backtests/{id}/orders', {
          params: { path: { id }, query: { after: pageParam, limit: 50 } },
        }),
      ),
    initialPageParam: 0,
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function backtestFillsQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: backtestKeys.facts(id, 'fills'),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/backtests/{id}/fills', {
          params: { path: { id }, query: { after: pageParam, limit: 50 } },
        }),
      ),
    initialPageParam: 0,
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function backtestDiagnosticsQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: backtestKeys.facts(id, 'diagnostics'),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/backtests/{id}/diagnostics', {
          params: { path: { id }, query: { after: pageParam, limit: 50 } },
        }),
      ),
    initialPageParam: 0,
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function strategyRunsQuery(strategyId: string) {
  return queryOptions({
    queryKey: backtestKeys.strategyRuns(strategyId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/strategies/{strategyId}/backtests', {
          params: { path: { strategyId }, query: { limit: 20 } },
        }),
      ),
  });
}

/**
 * The user's finished runs that pass the filters, best return first by default. Completed runs are listed only with a
 * valid quote currency; failed and cancelled runs are always ordered by start time.
 */
export function rankingQuery(
  filters: RankingFilters,
  sort: RankingSort = 'return',
  order: RankingOrder = 'desc',
) {
  const completed = filters.status === 'completed';
  const column = completed ? sort : 'created';
  const direction = completed ? order : 'desc';

  return infiniteQueryOptions({
    queryKey: backtestKeys.ranking(filters, column, direction),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/backtests', {
          params: {
            query: {
              ...rankingParameters(filters),
              limit: 50,
              sort: column,
              order: direction,
              ...(pageParam ? { after: pageParam } : {}),
            },
          },
        }),
      ),
    initialPageParam: '' as string,
    getNextPageParam: (page) => page.next ?? undefined,
    enabled: !completed || /^[A-Z0-9]{1,32}$/.test(filters.quote ?? ''),
  });
}

/** The values the user's finished runs take in each filter, with the number of runs of each. */
export function backtestFacetsQuery() {
  return queryOptions({
    queryKey: backtestKeys.facets,
    queryFn: async () => unwrap(await api.GET('/api/backtests/facets')),
  });
}

/** The query of the filters without absent values; the status is sent only when it is not the default. */
function rankingParameters(filters: RankingFilters) {
  const { status, minReturn, maxDrawdown, ...rest } = filters;
  const present = Object.fromEntries(
    Object.entries(rest).filter(([, value]) => value !== undefined && value !== ''),
  );

  return {
    ...(status === 'completed' ? {} : { status }),
    ...present,
    ...(status === 'completed' && minReturn ? { minReturn } : {}),
    ...(status === 'completed' && maxDrawdown ? { maxDrawdown } : {}),
  };
}
