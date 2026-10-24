import { drawable } from './pricePoint';

const point = (openTime: string, close = '105') => ({
  openTime,
  open: '100',
  high: '110.5',
  low: '90',
  close,
  equity: '1000.25',
});

describe('chart points', () => {
  it('draws candles and equity at UTC seconds', () => {
    expect(drawable([point('2026-01-01T00:00:00Z')])).toEqual({
      candles: [{ time: 1767225600, open: 100, high: 110.5, low: 90, close: 105 }],
      equity: [{ time: 1767225600, value: 1000.25 }],
    });
  });

  it('keeps times strictly increasing for the chart library', () => {
    const { candles } = drawable([
      point('2026-01-01T01:00:00Z'),
      point('2026-01-01T00:00:00Z'),
      point('2026-01-01T01:00:00Z', '1'),
      point('2026-01-01T02:00:00Z'),
    ]);

    expect(candles.map((candle) => candle.time)).toEqual([1767229200, 1767232800]);
  });
});
