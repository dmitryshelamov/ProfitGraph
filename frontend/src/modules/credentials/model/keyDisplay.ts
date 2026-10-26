import type { ExchangeCredential } from './types';

/** What a key's cell shows: the non-secret part in full, then a mask instead of the secret. */
export function credentialKeyDisplay(
  credential: Pick<ExchangeCredential, 'publicId' | 'apiKeyLast4'>,
) {
  return { visible: credential.publicId ?? `…${credential.apiKeyLast4}`, mask: '***' };
}
