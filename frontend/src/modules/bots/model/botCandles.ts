import type { Schemas } from '@/shared/api/client';

import type { BotCandlesPage, BotChartFill } from './types';

/** The length of each timeframe of the system, in seconds. */
export const timeframeSeconds: Readonly<Record<string, number>> = {
  '1m': 60,
  '3m': 180,
  '5m': 300,
  '15m': 900,
  '30m': 1800,
  '1h': 3600,
  '2h': 7200,
  '4h': 14_400,
  '6h': 21_600,
  '12h': 43_200,
  '1d': 86_400,
};

/** Candles the chart asks for at once when it goes back in history. */
export const chartPageCandles = 500;

/** The most candles one request may cover; the server refuses a longer range. */
export const maxPageCandles = 1000;

function duration(timeframe: string): number {
  return (timeframeSeconds[timeframe] ?? 3600) * 1000;
}

/** The half-open UTC range of {@link chartPageCandles} candles that ends at `to`. */
export function pageBefore(to: string, timeframe: string): { from: string; to: string } {
  const end = Date.parse(to);

  return {
    from: new Date(end - chartPageCandles * duration(timeframe)).toISOString(),
    to: new Date(end).toISOString(),
  };
}

/** Whether the candles closed since `from` no longer fit in one request, so the chart must start over from now. */
export function tooFarBehind(from: string, now: Date, timeframe: string): boolean {
  return now.getTime() - Date.parse(from) > (maxPageCandles - 1) * duration(timeframe);
}

/** The loaded pages as one history: each candle once by its open time and each fill once, oldest first. */
export function mergeCandlePages(pages: readonly BotCandlesPage[]): {
  candles: Schemas['CandleResponse'][];
  fills: BotChartFill[];
} {
  const candles = new Map<number, Schemas['CandleResponse']>();
  const fills = new Map<string, BotChartFill>();

  for (const page of pages) {
    for (const candle of page.candles) {
      candles.set(Date.parse(candle.openTime), candle);
    }

    for (const fill of page.fills) {
      fills.set(fill.id, fill);
    }
  }

  return {
    candles: [...candles.entries()]
      .sort(([left], [right]) => left - right)
      .map(([, candle]) => candle),
    fills: [...fills.values()].sort(
      (left, right) => Date.parse(left.filledAt) - Date.parse(right.filledAt),
    ),
  };
}
