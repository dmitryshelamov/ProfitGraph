import { acceptsCredential, venuesOfCredential } from './credentialVenues';
import type { Venue } from './types';

function venue(id: string, credentialVenue: string | null = null): Venue {
  return {
    id,
    name: id,
    timeframes: ['1h'],
    executionDefaults: null,
    botSettings: null,
    backtestLimits: [],
    credentialVenue,
  };
}

const venues = [venue('bybit'), venue('bybit-linear', 'bybit'), venue('tinvest')];

describe('credential venues', () => {
  it('lets a venue take its own keys and those of the venue it names', () => {
    expect(acceptsCredential(venues, 'bybit', 'bybit')).toBe(true);
    expect(acceptsCredential(venues, 'bybit-linear', 'bybit')).toBe(true);
    expect(acceptsCredential(venues, 'bybit-linear', 'bybit-linear')).toBe(true);
    expect(acceptsCredential(venues, 'bybit', 'bybit-linear')).toBe(false);
    expect(acceptsCredential(venues, 'bybit-linear', 'tinvest')).toBe(false);
  });

  it('lists every venue a key trades on', () => {
    expect(venuesOfCredential(venues, 'bybit').map((item) => item.id)).toEqual([
      'bybit',
      'bybit-linear',
    ]);
    expect(venuesOfCredential(venues, 'bybit-linear').map((item) => item.id)).toEqual([
      'bybit-linear',
    ]);
    expect(venuesOfCredential(undefined, 'bybit')).toEqual([]);
  });
});
