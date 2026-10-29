import type { Venue } from './types';

/**
 * Whether a key enrolled for `credentialVenue` serves `venue`: a key of the venue itself, or of the venue it names as its
 * `credentialVenue` (ADR 0061: one Bybit key trades Spot and futures).
 */
export function acceptsCredential(
  venues: readonly Venue[] | undefined,
  venue: string,
  credentialVenue: string,
): boolean {
  return (
    credentialVenue === venue ||
    venues?.find((item) => item.id === venue)?.credentialVenue === credentialVenue
  );
}

/** The venues a key enrolled for `credentialVenue` trades on, in the server's order. */
export function venuesOfCredential(
  venues: readonly Venue[] | undefined,
  credentialVenue: string,
): Venue[] {
  return (venues ?? []).filter((item) => acceptsCredential(venues, item.id, credentialVenue));
}
