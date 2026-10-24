import { useQuery } from '@tanstack/react-query';

import { type CurrentUser, currentUserQuery } from './api/accountsApi';

/** The signed-in user on protected screens; the router guarantees it is loaded before they render. */
export function useCurrentUser(): CurrentUser | null {
  return useQuery(currentUserQuery()).data ?? null;
}
