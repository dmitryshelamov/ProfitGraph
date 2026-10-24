import { queryOptions } from '@tanstack/react-query';

import { api, ApiError, type Schemas, unwrap } from '@/shared/api/client';

import type { Strategy, StrategyDefinition, StrategyLayout } from '../model/types';

export const strategyKeys = {
  all: ['strategies'] as const,
  detail: (id: string) => ['strategies', id] as const,
  versions: (id: string) => ['strategies', id, 'versions'] as const,
  validation: (id: string, version: number) => ['strategies', id, 'validation', version] as const,
  templates: ['strategy-templates'] as const,
  blockCatalog: ['block-catalog'] as const,
};

/** A missing strategy and a strategy of another user are both 404; retrying cannot change that. */
function retryUnlessFinal(count: number, error: Error): boolean {
  const final =
    error instanceof ApiError &&
    (error.status === 404 || error.code === 'unsupported_strategy_format');

  return !final && count < 2;
}

export function strategiesQuery() {
  return queryOptions({
    queryKey: strategyKeys.all,
    queryFn: async () => unwrap(await api.GET('/api/strategies')),
  });
}

export function strategyQuery(id: string) {
  return queryOptions({
    queryKey: strategyKeys.detail(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/strategies/{id}', { params: { path: { id } } })),
    retry: retryUnlessFinal,
  });
}

export function strategyVersionsQuery(id: string) {
  return queryOptions({
    queryKey: strategyKeys.versions(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/strategies/{id}/versions', { params: { path: { id } } })),
    retry: retryUnlessFinal,
  });
}

/** Validates a saved version; the result never changes for that version. */
export function strategyValidationQuery(id: string, version: number) {
  return queryOptions({
    queryKey: strategyKeys.validation(id, version),
    queryFn: async () =>
      unwrap(
        await api.POST('/api/strategies/{id}/validate', {
          params: { path: { id }, query: { version } },
        }),
      ),
    staleTime: Infinity,
    retry: retryUnlessFinal,
  });
}

export function templatesQuery() {
  return queryOptions({
    queryKey: strategyKeys.templates,
    queryFn: async () => unwrap(await api.GET('/api/strategy-templates')),
    staleTime: Infinity,
  });
}

export async function createStrategy(
  name: string,
  definition: StrategyDefinition,
  layout: StrategyLayout,
): Promise<Strategy> {
  // A template's definition and layout come from response types; the request types have the same shape.
  const body = { name, definition, layout } as Schemas['CreateStrategyRequest'];

  return unwrap(await api.POST('/api/strategies', { body }));
}

/** Blocks, checks and values the editor offers; it changes only with a new API version. */
export function blockCatalogQuery() {
  return queryOptions({
    queryKey: strategyKeys.blockCatalog,
    queryFn: async () => unwrap(await api.GET('/api/block-catalog')),
    staleTime: Infinity,
  });
}

export interface SaveStrategyInput {
  readonly id: string;
  readonly expectedVersion: number;
  readonly name: string;
  readonly definition: StrategyDefinition;
  readonly layout: StrategyLayout;
}

/** Saves the next version; fails with `strategy_changed` when someone saved a newer version first. */
export async function saveStrategy(input: SaveStrategyInput): Promise<Strategy> {
  const body = {
    expectedVersion: input.expectedVersion,
    name: input.name,
    definition: input.definition,
    layout: input.layout,
  } as Schemas['SaveStrategyRequest'];

  return unwrap(
    await api.PUT('/api/strategies/{id}', { params: { path: { id: input.id } }, body }),
  );
}

/** Validates an unsaved draft without storing it. */
export async function validateStrategy(id: string, definition: StrategyDefinition) {
  return unwrap(
    await api.POST('/api/strategies/{id}/validate', {
      params: { path: { id } },
      body: { definition } as Schemas['ValidateStrategyRequest'],
    }),
  );
}
