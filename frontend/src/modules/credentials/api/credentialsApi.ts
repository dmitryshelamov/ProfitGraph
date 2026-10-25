import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

import type {
  AuthenticatorCode,
  CredentialEnrollment,
  ExchangeCredential,
  VenueAccount,
} from '../model/types';

export const credentialKeys = {
  all: ['exchange-credentials'] as const,
  account: (id: string) => ['exchange-credentials', id, 'account'] as const,
  status: (id: string) => ['exchange-credentials', id, 'venue-status'] as const,
  protection: (id: string) => ['exchange-credentials', id, 'protection'] as const,
};

export function credentialsQuery() {
  return queryOptions({
    queryKey: credentialKeys.all,
    queryFn: async () => unwrap(await api.GET('/api/exchange-credentials')),
  });
}

/** An API key and secret, or for a venue whose key selects an account (T-Invest) its fields by name. */
export type CreateCredentialInput =
  | {
      readonly venue: string;
      readonly label: string;
      readonly apiKey: string;
      readonly apiSecret: string;
    }
  | {
      readonly venue: string;
      readonly label: string;
      readonly fields: Readonly<Record<string, string>>;
    };

/** Stores a key encrypted on the server; the response shows only a four-character public hint. */
export async function createCredential(input: CreateCredentialInput): Promise<ExchangeCredential> {
  const body =
    'fields' in input
      ? {
          venue: input.venue,
          label: input.label,
          apiKey: null,
          apiSecret: null,
          fields: { ...input.fields },
        }
      : { ...input };

  return unwrap(await api.POST('/api/exchange-credentials', { body }));
}

/**
 * The accounts a key not saved yet can trade through. The fields go in this request only and are never cached: this is
 * a plain call, not a query.
 */
export async function listVenueAccounts(
  venue: string,
  fields: Readonly<Record<string, string>>,
): Promise<VenueAccount[]> {
  return unwrap(
    await api.POST('/api/exchange-credentials/accounts', {
      body: { venue, fields: { ...fields } },
    }),
  );
}

/** Fails with `exchange_credential_in_use` while a bot uses the key. */
export async function deleteCredential(id: string): Promise<void> {
  const result = await api.DELETE('/api/exchange-credentials/{id}', { params: { path: { id } } });

  if (!result.response.ok) {
    unwrap(result);
  }
}

/** The account value history of a key's exchange account over the last week, oldest first, with how reading it goes. */
export function accountHistoryQuery(id: string) {
  return queryOptions({
    queryKey: credentialKeys.account(id),
    queryFn: async () =>
      unwrap(await api.GET('/api/exchange-credentials/{id}/account', { params: { path: { id } } })),
    refetchInterval: 60_000,
  });
}

/** The venue's own status of a key's account, such as a Steam Guard still being checked. */
export function accountStatusQuery(id: string) {
  return queryOptions({
    queryKey: credentialKeys.status(id),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/exchange-credentials/{id}/venue-status', { params: { path: { id } } }),
      ),
    refetchInterval: 60_000,
  });
}

/** Whether the venue account's protection holds the key's bots back, and why. */
export function accountProtectionQuery(id: string) {
  return queryOptions({
    queryKey: credentialKeys.protection(id),
    queryFn: async () =>
      unwrap(
        await api.GET('/api/exchange-credentials/{id}/protection', { params: { path: { id } } }),
      ),
    refetchInterval: 60_000,
  });
}

export async function resumeProtection(id: string): Promise<void> {
  const result = await api.POST('/api/exchange-credentials/{id}/protection/resume', {
    params: { path: { id } },
  });

  if (!result.response.ok) {
    unwrap(result);
  }
}

/**
 * The current code of a key's authenticator, for the Owner only. A plain call, never a query: the code is not cached.
 */
export async function authenticatorCode(id: string): Promise<AuthenticatorCode> {
  return unwrap(
    await api.GET('/api/exchange-credentials/{id}/authenticator-code', {
      params: { path: { id } },
    }),
  );
}

/**
 * Starts creating a key with a venue that enrolls them in steps. The fields go in this request only: a plain call, not
 * a mutation cache entry the fields could stay in.
 */
export async function startEnrollment(
  venue: string,
  label: string,
  fields: Readonly<Record<string, string>>,
): Promise<CredentialEnrollment> {
  return unwrap(
    await api.POST('/api/exchange-credentials/enrollments', {
      body: { venue, label, fields: { ...fields } },
    }),
  );
}

/** Answers the step an enrollment waits for with that step's fields. */
export async function continueEnrollment(
  venue: string,
  id: string,
  step: string,
  fields: Readonly<Record<string, string>>,
): Promise<CredentialEnrollment> {
  return unwrap(
    await api.POST('/api/exchange-credentials/enrollments/{id}/steps', {
      params: { path: { id } },
      body: { venue, step, fields: { ...fields } },
    }),
  );
}
