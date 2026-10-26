import { drawableCandles } from './candleHistory';

const candle = (openTime: string, close: string) => ({
  openTime,
  open: close,
  high: close,
  low: close,
  close,
});

describe('candle history', () => {
  it('keeps each open time once, oldest first', () => {
    const drawn = drawableCandles([
      candle('2026-10-24T10:00:00Z', '3'),
      candle('2026-10-24T08:00:00Z', '1'),
      candle('2026-10-24T10:00:00Z', '3'),
      candle('2026-10-24T09:00:00Z', '2'),
    ]);

    expect(drawn.map((item) => item.close)).toEqual([1, 2, 3]);
  });
});
