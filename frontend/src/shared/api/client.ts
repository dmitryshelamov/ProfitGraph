import createClient from 'openapi-fetch';

import type { components, paths } from './generated/schema';

export type Schemas = components['schemas'];

/**
 * The only HTTP client. Requests go to the page's own origin; in development the Vite proxy forwards them to the API.
 * `fetch` is resolved on every call, so a replaced global (tests, instrumentation) is honoured.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  fetch: (request) => globalThis.fetch(request),
});

/** A normalized API error built from the backend Problem Details (`code`, `errors`). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly errors: readonly ApiFieldError[] = [],
    readonly payload?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface ApiFieldError {
  readonly code: string;
  readonly field: string;
  readonly message: string;
}

interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
  readonly errors?: readonly ApiFieldError[];
}

/** Returns the data of a successful response or throws ApiError. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok && result.data !== undefined) {
    return result.data;
  }

  const problem = (result.error ?? {}) as ProblemDetails;

  throw new ApiError(
    result.response.status,
    problem.code ?? 'http_' + result.response.status,
    problem.detail ?? problem.title ?? result.response.statusText,
    problem.errors ?? [],
    problem,
  );
}
