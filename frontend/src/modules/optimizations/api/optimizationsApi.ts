import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api, ApiError, type Schemas, unwrap } from '@/shared/api/client';

import type { StartOptimizationRequest } from '../model/types';

export const optimizationKeys = {
  optimization: (id: string) => ['optimizations', id] as const,
  trials: (id: string) => ['optimizations', id, 'trials'] as const,
  trial: (id: string, windowIndex: number, trialIndex: number) =>
    ['optimizations', id, 'trials', windowIndex, trialIndex] as const,
  windows: (id: string) => ['optimizations', id, 'windows'] as const,
  strategyOptimizations: (strategyId: string) =>
    ['strategies', strategyId, 'optimizations'] as const,
  lists: ['optimizations', 'list'] as const,
  list: (filters: OptimizationFilters) => ['optimizations', 'list', filters] as const,
};

/** Where a listed optimization stands; `active` covers queued, running and paused ones. */
export type OptimizationListState = 'active' | 'completed' | 'failed' | 'cancelled';

/** The optimization list's filters; an absent value filters nothing. */
export interface OptimizationFilters {
  state?: OptimizationListState;
  strategyId?: string;
  venue?: string;
  timeframe?: string;
  search?: string;
  method?: string;
}

/** Another user's optimization is a 404 like a missing one; retrying cannot change that. */
function retryUnlessMissing(count: number, error: Error): boolean {
  return !(error instanceof ApiError && error.status === 404) && count < 2;
}

/** Freezes a search around a saved strategy version and queues it; the optimization has the job's identifier. */
export async function startOptimization(
  request: StartOptimizationRequest,
): Promise<Schemas['BackgroundJobResponse']> {
  return unwrap(await api.POST('/api/optimizations', { body: request }));
}

export async function pauseOptimization(id: string): Promise<Schemas['BackgroundJobResponse']> {
  return unwrap(await api.POST('/api/optimizations/{id}/pause', { params: { path: { id } } }));
}

export async function resumeOptimization(id: string): Promise<Schemas['BackgroundJobResponse']> {
  return unwrap(await api.POST('/api/optimizations/{id}/resume', { params: { path: { id } } }));
}

export function optimizationQuery(id: string) {
  return queryOptions({
    queryKey: optimizationKeys.optimization(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/optimizations/{id}', { params: { path: { id } } })),
    retry: retryUnlessMissing,
  });
}

export function optimizationWindowsQuery(id: string) {
  return queryOptions({
    queryKey: optimizationKeys.windows(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/optimizations/{id}/windows', { params: { path: { id } } })),
    retry: retryUnlessMissing,
  });
}

/** Stored trials after a delivery sequence; a client fills gaps in progress messages with it. */
export async function optimizationTrials(id: string, after: number, limit = 500) {
  return unwrap(
    await api.GET('/api/optimizations/{id}/trials', {
      params: { path: { id }, query: { after, limit } },
    }),
  );
}

/** All stored trials, page by page in delivery order. */
export function optimizationTrialsQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: optimizationKeys.trials(id),
    queryFn: async ({ pageParam }) => optimizationTrials(id, pageParam),
    initialPageParam: 0,
    getNextPageParam: (page) => page.next ?? undefined,
    retry: retryUnlessMissing,
  });
}

/** One stored trial by its index in the search and its walk-forward window. */
export function optimizationTrialQuery(id: string, windowIndex: number, trialIndex: number) {
  return queryOptions({
    queryKey: optimizationKeys.trial(id, windowIndex, trialIndex),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/optimizations/{id}/trials/{trialIndex}', {
          params: { path: { id, trialIndex }, query: { window: windowIndex } },
        }),
      ),
    retry: retryUnlessMissing,
  });
}

export function strategyOptimizationsQuery(strategyId: string) {
  return queryOptions({
    queryKey: optimizationKeys.strategyOptimizations(strategyId),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/strategies/{strategyId}/optimizations', {
          params: { path: { strategyId }, query: { limit: 20 } },
        }),
      ),
  });
}

/** The user's optimizations of every strategy that pass the filters, newest first. */
export function optimizationsQuery(filters: OptimizationFilters) {
  return infiniteQueryOptions({
    queryKey: optimizationKeys.list(filters),
    queryFn: async ({ pageParam }) =>
      unwrap(
        await api.GET('/api/optimizations', {
          params: {
            query: {
              ...Object.fromEntries(
                Object.entries(filters).filter(([, value]) => value !== undefined && value !== ''),
              ),
              limit: 50,
              ...(pageParam ? { after: pageParam } : {}),
            },
          },
        }),
      ),
    initialPageParam: '' as string,
    getNextPageParam: (page) => page.next ?? undefined,
  });
}
