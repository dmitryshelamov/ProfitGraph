import type { DrawnCandle } from './pricePoint';

/** A closed candle as the API sends it: an open time and exact decimal strings. */
export interface HistoryCandle {
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
}

/** A trade to mark on the chart: when, which side and at what exact price. */
export interface ChartTrade {
  readonly id: string;
  readonly time: string;
  readonly side: 'Buy' | 'Sell';
  readonly price: string;
  readonly text: string;
}

/** A trade placed on the candle in which it happened, at its price. */
export interface DrawnTrade {
  readonly id: string;
  readonly time: number;
  readonly side: 'Buy' | 'Sell';
  readonly price: number;
  readonly text: string;
}

/** A horizontal level, such as a working limit order. */
export interface ChartLevel {
  readonly id: string;
  readonly side: 'Buy' | 'Sell';
  readonly price: string;
  readonly title: string;
}

function seconds(time: string): number {
  return Math.floor(Date.parse(time) / 1000);
}

/**
 * Candles as numbers for drawing, oldest first, each open time once; the exact values stay in the strings.
 */
export function drawableCandles(candles: readonly HistoryCandle[]): DrawnCandle[] {
  const byTime = new Map<number, DrawnCandle>();

  for (const candle of candles) {
    const time = seconds(candle.openTime);

    if (Number.isFinite(time)) {
      byTime.set(time, {
        time,
        open: Number(candle.open),
        high: Number(candle.high),
        low: Number(candle.low),
        close: Number(candle.close),
      });
    }
  }

  return [...byTime.values()].sort((left, right) => left.time - right.time);
}

/**
 * Places each trade on the latest candle that opened at or before it, in time order as the chart library requires.
 * A trade before the first drawn candle is left out until older candles are loaded.
 */
export function drawableTrades(
  candles: readonly DrawnCandle[],
  trades: readonly ChartTrade[],
): DrawnTrade[] {
  const placed: DrawnTrade[] = [];

  for (const trade of trades) {
    const at = seconds(trade.time);
    let low = 0;
    let high = candles.length - 1;
    let found: DrawnCandle | undefined;

    while (low <= high) {
      const middle = (low + high) >> 1;
      const candle = candles[middle];

      if (candle && candle.time <= at) {
        found = candle;
        low = middle + 1;
      } else {
        high = middle - 1;
      }
    }

    if (found) {
      placed.push({
        id: trade.id,
        time: found.time,
        side: trade.side,
        price: Number(trade.price),
        text: trade.text,
      });
    }
  }

  return placed.sort((left, right) => left.time - right.time);
}
