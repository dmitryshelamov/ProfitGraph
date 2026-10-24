import type { DrawnValue } from './pricePoint';

/** One account's equity after a candle or a bucket of candles. */
export interface EquityPoint {
  readonly time: string;
  readonly equity: string;
}

/** Equity as the chart library's numbers, for drawing only; times must strictly increase, so repeats are dropped. */
export function drawableEquity(points: readonly EquityPoint[]): DrawnValue[] {
  const values: DrawnValue[] = [];
  let previous = Number.NEGATIVE_INFINITY;

  for (const point of points) {
    const time = Math.floor(Date.parse(point.time) / 1000);

    if (Number.isFinite(time) && time > previous) {
      previous = time;
      values.push({ time, value: Number(point.equity) });
    }
  }

  return values;
}
