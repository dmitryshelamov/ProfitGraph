/** One chart point: a candle, or a bucket of consecutive candles, with the account's equity after it. */
export interface PricePoint {
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly equity: string;
}

export interface DrawnCandle {
  readonly time: number;
  readonly open: number;
  readonly high: number;
  readonly low: number;
  readonly close: number;
}

export interface DrawnValue {
  readonly time: number;
  readonly value: number;
}

/**
 * Turns exact decimal strings into the numbers the chart library draws. This is the only place they become numbers:
 * the chart shows shapes, and every exact value on the page comes from the strings.
 */
export function drawable(points: readonly PricePoint[]): {
  candles: DrawnCandle[];
  equity: DrawnValue[];
} {
  const candles: DrawnCandle[] = [];
  const equity: DrawnValue[] = [];
  let previous = Number.NEGATIVE_INFINITY;

  for (const point of points) {
    const time = Math.floor(Date.parse(point.openTime) / 1000);

    // The library needs strictly increasing times; a repeated or out-of-order point is dropped.
    if (!Number.isFinite(time) || time <= previous) {
      continue;
    }

    previous = time;
    candles.push({
      time,
      open: Number(point.open),
      high: Number(point.high),
      low: Number(point.low),
      close: Number(point.close),
    });
    equity.push({ time, value: Number(point.equity) });
  }

  return { candles, equity };
}
