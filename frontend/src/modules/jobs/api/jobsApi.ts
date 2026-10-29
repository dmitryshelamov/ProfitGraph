import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

import { isJobFinished } from '../model/jobStatus';
import type { BackgroundJob } from '../model/types';

export const jobKeys = {
  detail: (id: string) => ['jobs', id] as const,
  active: ['jobs', 'active'] as const,
};

/** Polls a job every second until it finishes; a finished job never changes. */
export function jobQuery(id: string) {
  return queryOptions({
    queryKey: jobKeys.detail(id),
    queryFn: async () => unwrap(await api.GET('/api/jobs/{id}', { params: { path: { id } } })),
    refetchInterval: (query) =>
      query.state.data && isJobFinished(query.state.data) ? false : 1000,
  });
}

/**
 * The user's queued, running and paused jobs with their places in the Worker's lanes, polled every two seconds while
 * there are any and every ten seconds otherwise.
 */
export function activeJobsQuery() {
  return queryOptions({
    queryKey: jobKeys.active,
    queryFn: async () => unwrap(await api.GET('/api/jobs/active')),
    refetchInterval: (query) => (query.state.data?.jobs.length ? 2000 : 10_000),
  });
}

/** Asks the Worker to stop the job; a queued job is cancelled at once, a running one when the Worker sees the request. */
export async function cancelJob(id: string): Promise<BackgroundJob> {
  return unwrap(await api.POST('/api/jobs/{id}/cancel', { params: { path: { id } } }));
}
