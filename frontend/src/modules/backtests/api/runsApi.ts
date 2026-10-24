import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api, ApiError, unwrap } from '@/shared/api/client';

export const backtestKeys = {
  run: (id: string) => ['backtests', id] as const,
  chart: (id: string) => ['backtests', id, 'chart'] as const,
  facts: (id: string, kind: FactKind) => ['backtests', id, kind] as const,
  strategyRuns: (strategyId: string) => ['strategies', strategyId, 'backtests'] as const,
  ranking: (quote: string, sort: RankingSort, order: RankingOrder) =>
    ['backtests', 'ranking', quote, sort, order] as const,
};

export type FactKind = 'orders' | 'fills' | 'diagnostics';

/** The ranking's column and direction; ties fall back to the start time in the same direction. */
export type RankingSort = 'return' | 'pnl' | 'drawdown' | 'created';
export type RankingOrder = 'asc' | 'desc';

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

/** The user's completed runs in one quote currency, best return first. */
export function rankingQuery(
  quote: string,
  sort: RankingSort = 'return',
  order: RankingOrder = 'desc',
) {
  return infiniteQueryOptions({
    queryKey: backtestKeys.ranking(quote, sort, order),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/backtests', {
          params: {
            query: { quote, limit: 50, sort, order, ...(pageParam ? { after: pageParam } : {}) },
          },
        }),
      ),
    initialPageParam: '' as string,
    getNextPageParam: (page) => page.next ?? undefined,
    enabled: /^[A-Z0-9]{1,32}$/.test(quote),
  });
}
