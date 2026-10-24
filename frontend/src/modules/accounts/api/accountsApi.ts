import { type QueryClient, queryOptions } from '@tanstack/react-query';

import { api, type Schemas, unwrap } from '@/shared/api/client';

export type CurrentUser = Schemas['UserResponse'];

export const accountKeys = {
  currentUser: ['accounts', 'current-user'] as const,
};

/** The signed-in user, or null without a session. Other failures are errors, not a signed-out state. */
export function currentUserQuery() {
  return queryOptions({
    queryKey: accountKeys.currentUser,
    queryFn: async (): Promise<CurrentUser | null> => {
      const result = await api.GET('/api/auth/me');

      return result.response.status === 401 ? null : unwrap(result);
    },
    staleTime: Infinity,
  });
}

export type SignInResult = { readonly user: CurrentUser } | { readonly failure: string };

/** Signs in and replaces every cached query, so no data of a previous user survives. */
export async function signIn(
  queryClient: QueryClient,
  login: string,
  password: string,
): Promise<SignInResult> {
  const result = await api.POST('/api/auth/login', { body: { login, password } });

  if (result.response.status === 401) {
    return { failure: 'Неверный логин или пароль.' };
  }

  if (result.response.status === 429) {
    return { failure: 'Слишком много попыток. Попробуйте позже.' };
  }

  if (!result.response.ok || !result.data) {
    return { failure: 'Не удалось войти. Попробуйте позже.' };
  }

  queryClient.clear();
  queryClient.setQueryData(accountKeys.currentUser, result.data);

  return { user: result.data };
}

export async function signOut(queryClient: QueryClient): Promise<void> {
  await api.POST('/api/auth/logout');
  forgetSession(queryClient);
}

/** Drops all cached data of the session; used after sign-out and when the server reports an expired session. */
export function forgetSession(queryClient: QueryClient): void {
  queryClient.clear();
  queryClient.setQueryData(accountKeys.currentUser, null);
}
