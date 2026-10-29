import { mergeCandlePages } from './botCandles';
import type { BotCandlesPage } from './types';

const candle = (openTime: string) => ({
  openTime,
  open: '1',
  high: '1',
  low: '1',
  close: '1',
  volume: '0',
});

const page = (
  candles: string[],
  fills: { id: string; filledAt: string }[] = [],
): BotCandlesPage => ({
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1h',
  from: '2026-10-24T00:00:00Z',
  to: '2026-10-25T00:00:00Z',
  latestClosed: '2026-10-25T00:00:00Z',
  status: 'Complete',
  errorCode: null,
  candles: candles.map(candle),
  fills: fills.map((fill) => ({ ...fill, side: 'Buy', price: '1', grossBase: '1', tag: null })),
  fillsTruncated: false,
});

describe('bot candles', () => {
  it('merges pages into one history without repeats, oldest first', () => {
    const merged = mergeCandlePages([
      page(
        ['2026-10-24T10:00:00Z', '2026-10-24T11:00:00Z'],
        [{ id: 'b', filledAt: '2026-10-24T11:10:00Z' }],
      ),
      page(
        ['2026-10-24T09:00:00Z', '2026-10-24T10:00:00Z'],
        [{ id: 'a', filledAt: '2026-10-24T09:30:00Z' }],
      ),
      page(['2026-10-24T11:00:00Z'], [{ id: 'b', filledAt: '2026-10-24T11:10:00Z' }]),
    ]);

    expect(merged.candles.map((item) => item.openTime)).toEqual([
      '2026-10-24T09:00:00Z',
      '2026-10-24T10:00:00Z',
      '2026-10-24T11:00:00Z',
    ]);
    expect(merged.fills.map((fill) => fill.id)).toEqual(['a', 'b']);
  });
});
