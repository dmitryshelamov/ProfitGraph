import { timeframeSeconds } from '@/modules/market-chart';

/** The timeframes a bot's chart offers besides the bot's own, shortest first. */
export const botChartTimeframes = ['1m', '5m', '15m', '1h', '4h', '1d'] as const;

/** A timeframe from the address if it is one the system knows; whether the venue offers it is checked on the page. */
export function botChartTimeframe(value: unknown): string | undefined {
  return typeof value === 'string' && value in timeframeSeconds ? value : undefined;
}

/**
 * The timeframes the bot's chart switches between, shortest first: the chart's own set and the bot's own timeframe, cut to
 * what the venue offers once that is known. The bot's own timeframe is always there, even on a venue that does not list it.
 */
export function chartTimeframesFor(own: string, venueTimeframes?: readonly string[]): string[] {
  const offered = (venueTimeframes ?? botChartTimeframes).filter(
    (item) => (botChartTimeframes as readonly string[]).includes(item) && item in timeframeSeconds,
  );

  return [...new Set([...offered, own])].sort(
    (left, right) =>
      (timeframeSeconds[left] ?? Number.POSITIVE_INFINITY) -
      (timeframeSeconds[right] ?? Number.POSITIVE_INFINITY),
  );
}

/** The timeframe the chart shows: the one asked for when the chart offers it, otherwise the bot's own. */
export function shownTimeframe(
  asked: string | undefined,
  own: string,
  offered: readonly string[],
): string {
  return asked !== undefined && offered.includes(asked) ? asked : own;
}
