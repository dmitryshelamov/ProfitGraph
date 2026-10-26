import { drawableCandles } from './candleHistory';
import { drawableLive, withAlpha } from './liveCandles';

describe('live candles', () => {
  const closed = drawableCandles([
    { openTime: '2026-10-26T09:00:00Z', open: '1', high: '2', low: '1', close: '2' },
    { openTime: '2026-10-26T09:01:00Z', open: '2', high: '3', low: '2', close: '3' },
  ]);
  const live = (openTime: string, isClosed: boolean) => ({
    openTime,
    open: '3',
    high: '4',
    low: '3',
    close: '4',
    isClosed,
  });

  it('draws live candles after the history, the unclosed one marked as forming', () => {
    expect(
      drawableLive(closed, [
        live('2026-10-26T09:02:00Z', true),
        live('2026-10-26T09:03:00Z', false),
      ]).map((candle) => [candle.time, candle.forming]),
    ).toEqual([
      [Date.parse('2026-10-26T09:02:00Z') / 1000, false],
      [Date.parse('2026-10-26T09:03:00Z') / 1000, true],
    ]);
  });

  it('does not draw a candle the history already has', () => {
    expect(
      drawableLive(closed, [
        live('2026-10-26T09:01:00Z', true),
        live('2026-10-26T09:00:00Z', false),
        live('bad', false),
      ]),
    ).toEqual([]);
  });

  it('draws live candles alone while there is no history yet', () => {
    expect(drawableLive([], [live('2026-10-26T09:00:00Z', false)])).toHaveLength(1);
  });

  it('makes a hex colour see-through and leaves any other colour alone', () => {
    expect(withAlpha('#3fd393', 0.5)).toBe('rgba(63, 211, 147, 0.5)');
    expect(withAlpha('rgb(1, 2, 3)', 0.5)).toBe('rgb(1, 2, 3)');
  });
});
