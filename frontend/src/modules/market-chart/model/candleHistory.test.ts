import { drawableCandles, drawableTrades } from './candleHistory';

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

  it('puts a trade on the candle it happened in and leaves out one before the first candle', () => {
    const candles = drawableCandles([
      candle('2026-10-24T08:00:00Z', '1'),
      candle('2026-10-24T09:00:00Z', '2'),
      // 10:00 had no candle, as a Steam hour without sales.
      candle('2026-10-24T11:00:00Z', '4'),
    ]);

    const trades = drawableTrades(candles, [
      { id: 'b', time: '2026-10-24T11:30:00Z', side: 'Sell', price: '4.5', text: '4.5' },
      { id: 'a', time: '2026-10-24T08:59:59Z', side: 'Buy', price: '1.25', text: '1.25' },
      { id: 'c', time: '2026-10-24T10:15:00Z', side: 'Buy', price: '2', text: '2' },
      { id: 'early', time: '2026-10-24T07:00:00Z', side: 'Buy', price: '1', text: '1' },
    ]);

    expect(trades.map((trade) => [trade.id, new Date(trade.time * 1000).toISOString()])).toEqual([
      ['a', '2026-10-24T08:00:00.000Z'],
      ['c', '2026-10-24T09:00:00.000Z'],
      ['b', '2026-10-24T11:00:00.000Z'],
    ]);
    expect(trades[0]?.price).toBe(1.25);
  });
});
