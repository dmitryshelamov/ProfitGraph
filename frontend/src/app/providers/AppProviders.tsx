import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type RouterHistory, RouterProvider } from '@tanstack/react-router';
import { useState } from 'react';

import { createAppRouter } from '@/app/router/router';
import { forgetSession } from '@/modules/accounts';
import { ApiError } from '@/shared/api/client';

export function createQueryClient(onUnauthorized: () => void): QueryClient {
  const handle = (error: Error) => {
    if (error instanceof ApiError && error.status === 401) {
      onUnauthorized();
    }
  };

  return new QueryClient({
    queryCache: new QueryCache({ onError: handle }),
    mutationCache: new MutationCache({ onError: handle }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 1,
        refetchOnWindowFocus: false,
      },
    },
  });
}

/**
 * Wires the query client and the router. A 401 from any request means the session has expired: cached data is
 * dropped and the user signs in again, returning to the same screen.
 */
export function createAppContext(history?: RouterHistory) {
  const queryClient: QueryClient = createQueryClient(() => {
    forgetSession(queryClient);
    void router.navigate({ to: '/sign-in', search: { returnTo: router.state.location.href } });
  });
  const router = createAppRouter(queryClient, history);

  return { queryClient, router };
}

export function AppProviders({ history }: { history?: RouterHistory }) {
  const [{ queryClient, router }] = useState(() => createAppContext(history));

  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}
