import { drawableCandles, pricePrecision } from './candleHistory';

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

describe('pricePrecision', () => {
  const at = (price: string) => candle('2026-10-27T00:00:00Z', price);

  it('uses the places the instrument is known by', () => {
    expect(pricePrecision(5, [at('0.03')])).toBe(5);
  });

  it('follows the candles when the places are unknown', () => {
    expect(pricePrecision(null, [at('0.03596'), at('0.0360')])).toBe(5);
    expect(pricePrecision(null, [at('114.4')])).toBe(2);
    expect(pricePrecision(null, [])).toBe(2);
  });
});
