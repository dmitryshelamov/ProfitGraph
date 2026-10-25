/**
 * A history the user picks: venue, pair as typed (the venue normalizes it) and timeframe, and the user's key of that venue
 * that reads its market data when the venue has none anonymous (T-Invest).
 */
export interface SeriesSelection {
  readonly venue: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly credentialId?: string;
}

/** Venues whose market data is read with the user's own key. */
export const venuesReadWithKey: ReadonlySet<string> = new Set(['tinvest']);

const timeframeLabels: Record<string, string> = {
  '1m': '1 минута',
  '3m': '3 минуты',
  '5m': '5 минут',
  '15m': '15 минут',
  '30m': '30 минут',
  '1h': '1 час',
  '2h': '2 часа',
  '4h': '4 часа',
  '6h': '6 часов',
  '12h': '12 часов',
  '1d': '1 день',
};

export function timeframeLabel(timeframe: string): string {
  return timeframeLabels[timeframe] ?? timeframe;
}
