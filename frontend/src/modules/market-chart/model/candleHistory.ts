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

/** A horizontal level, such as a working limit order. */
export interface ChartLevel {
  readonly id: string;
  readonly side: 'Buy' | 'Sell';
  readonly price: string;
  readonly title: string;
}

export function seconds(time: string): number {
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

/** The most decimal places a price may be drawn with. */
const maxPricePlaces = 10;

/**
 * How many decimal places the price scale shows: the instrument's own when known, otherwise as many as the newest candles'
 * exact prices carry, so a price of 0.0359 is not rounded to 0.04 and the scale's steps stay apart.
 */
export function pricePrecision(known: number | null, candles: readonly HistoryCandle[]): number {
  if (known !== null) {
    return Math.min(Math.max(known, 0), maxPricePlaces);
  }

  let places = 2;

  for (const candle of candles.slice(-200)) {
    for (const price of [candle.open, candle.high, candle.low, candle.close]) {
      const dot = price.indexOf('.');

      if (dot >= 0) {
        places = Math.max(places, price.replace(/0+$/, '').length - dot - 1);
      }
    }
  }

  return Math.min(places, maxPricePlaces);
}
