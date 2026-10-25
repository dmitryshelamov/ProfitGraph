import { queryOptions } from '@tanstack/react-query';

import { api, unwrap } from '@/shared/api/client';

import type { ExchangeCredential } from '../model/types';

export const credentialKeys = {
  all: ['exchange-credentials'] as const,
};

export function credentialsQuery() {
  return queryOptions({
    queryKey: credentialKeys.all,
    queryFn: async () => unwrap(await api.GET('/api/exchange-credentials')),
  });
}

export interface CreateCredentialInput {
  readonly venue: string;
  readonly label: string;
  readonly apiKey: string;
  readonly apiSecret: string;
}

/** Stores a key encrypted on the server; the response shows only the key's last four characters. */
export async function createCredential(input: CreateCredentialInput): Promise<ExchangeCredential> {
  return unwrap(await api.POST('/api/exchange-credentials', { body: { ...input } }));
}

/** Fails with `exchange_credential_in_use` while a bot uses the key. */
export async function deleteCredential(id: string): Promise<void> {
  const result = await api.DELETE('/api/exchange-credentials/{id}', { params: { path: { id } } });

  if (!result.response.ok) {
    unwrap(result);
  }
}
