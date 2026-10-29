import { mergeCandles, pageBefore, tooFarBehind } from './chartPaging';

const candle = (openTime: string) => ({ openTime, open: '1', high: '1', low: '1', close: '1' });

describe('chart paging', () => {
  it('asks for a page of 500 candles before the given end', () => {
    expect(pageBefore('2026-10-25T00:00:00.000Z', '1h')).toEqual({
      from: '2026-10-04T04:00:00.000Z',
      to: '2026-10-25T00:00:00.000Z',
    });
    expect(pageBefore('2026-10-25T00:00:00.000Z', '1m').from).toBe('2026-10-24T15:40:00.000Z');
  });

  it('merges pages into one history without repeats, oldest first', () => {
    const merged = mergeCandles([
      { candles: [candle('2026-10-24T10:00:00Z'), candle('2026-10-24T11:00:00Z')] },
      { candles: [candle('2026-10-24T09:00:00Z'), candle('2026-10-24T10:00:00Z')] },
    ]);

    expect(merged.map((item) => item.openTime)).toEqual([
      '2026-10-24T09:00:00Z',
      '2026-10-24T10:00:00Z',
      '2026-10-24T11:00:00Z',
    ]);
  });

  it('starts over when the candles closed since no longer fit in one request', () => {
    const now = new Date('2026-10-25T00:00:00Z');

    expect(tooFarBehind('2026-10-24T06:00:00Z', now, '1m')).toBe(true);
    expect(tooFarBehind('2026-10-24T06:00:00Z', now, '1h')).toBe(false);
  });
});
