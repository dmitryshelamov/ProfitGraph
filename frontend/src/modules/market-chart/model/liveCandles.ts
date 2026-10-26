import { drawableCandles, type HistoryCandle } from './candleHistory';
import type { DrawnCandle } from './pricePoint';

/** A candle still being formed, or just closed, in memory only; it is drawn on top of the closed history. */
export interface LiveCandle extends HistoryCandle {
  readonly isClosed: boolean;
}

/** A live candle ready to draw; `forming` marks the one that is not closed yet. */
export interface DrawnLiveCandle extends DrawnCandle {
  readonly forming: boolean;
}

/**
 * The live candles to draw after the closed history, oldest first, each open time once. The history stays authoritative: a live
 * candle that opened at or before its last candle is left out, so a candle that has since closed is never drawn twice.
 */
export function drawableLive(
  candles: readonly DrawnCandle[],
  live: readonly LiveCandle[],
): DrawnLiveCandle[] {
  const last = candles.at(-1)?.time ?? Number.NEGATIVE_INFINITY;
  const byTime = new Map<number, DrawnLiveCandle>();

  for (const candle of live) {
    const [drawn] = drawableCandles([candle]);

    if (drawn && drawn.time > last) {
      byTime.set(drawn.time, { ...drawn, forming: !candle.isClosed });
    }
  }

  return [...byTime.values()].sort((left, right) => left.time - right.time);
}

/** A `#rrggbb` colour with transparency, for the candle that is not closed; any other colour is returned as it is. */
export function withAlpha(colour: string, alpha: number): string {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(colour.trim());

  if (!match) {
    return colour;
  }

  const [, red = '0', green = '0', blue = '0'] = match;

  return `rgba(${parseInt(red, 16)}, ${parseInt(green, 16)}, ${parseInt(blue, 16)}, ${alpha})`;
}
