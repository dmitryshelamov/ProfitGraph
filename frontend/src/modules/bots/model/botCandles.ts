import { mergeCandles } from '@/modules/market-chart';
import type { Schemas } from '@/shared/api/client';

import type { BotCandlesPage, BotChartFill } from './types';

/** The loaded pages as one history: each candle once by its open time and each fill once, oldest first. */
export function mergeCandlePages(pages: readonly BotCandlesPage[]): {
  candles: Schemas['CandleResponse'][];
  fills: BotChartFill[];
} {
  const fills = new Map<string, BotChartFill>();

  for (const page of pages) {
    for (const fill of page.fills) {
      fills.set(fill.id, fill);
    }
  }

  return {
    candles: mergeCandles(pages),
    fills: [...fills.values()].sort(
      (left, right) => Date.parse(left.filledAt) - Date.parse(right.filledAt),
    ),
  };
}
