import { act, renderHook } from '@testing-library/react';

import { followFormingCandle, type FormingHandlers } from '../realtime/followFormingCandle';
import type { FormingCandle } from './formingCandle';
import { useFormingCandle } from './useFormingCandle';

vi.mock('../realtime/followFormingCandle', () => ({ followFormingCandle: vi.fn() }));

const candle: FormingCandle = {
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1m',
  openTime: '2026-10-26T09:00:00Z',
  open: '100',
  high: '110',
  low: '90',
  close: '105',
  volume: '5',
  isClosed: false,
  observedAt: '2026-10-26T09:00:03Z',
};

describe('useFormingCandle', () => {
  let handlers: FormingHandlers;
  const stop = vi.fn();

  beforeEach(() => {
    stop.mockReset();
    vi.mocked(followFormingCandle).mockImplementation((_botId, next) => {
      handlers = next;

      return stop;
    });
  });

  it('follows the bot, shows its candle and the state of the channel', () => {
    const { result } = renderHook(() => useFormingCandle('bot-1'));

    expect(followFormingCandle).toHaveBeenCalledWith('bot-1', expect.any(Object));
    expect(result.current).toEqual({ feed: 'connecting', live: [] });

    act(() => handlers.onCandle(candle));
    expect(result.current).toEqual({ feed: 'live', live: [candle] });

    act(() => handlers.onLost());
    expect(result.current.feed).toBe('noData');
    expect(result.current.live).toEqual([candle]);

    act(() =>
      handlers.onSnapshot({
        venue: 'bybit',
        symbol: 'BTCUSDT',
        timeframe: '1m',
        status: 'Live',
        candle,
      }),
    );
    expect(result.current.feed).toBe('live');
  });

  it('stops following when the page closes and starts over for another bot', () => {
    const { result, rerender, unmount } = renderHook(({ id }) => useFormingCandle(id), {
      initialProps: { id: 'bot-1' },
    });
    act(() => handlers.onCandle(candle));

    rerender({ id: 'bot-2' });

    expect(stop).toHaveBeenCalledTimes(1);
    expect(result.current).toEqual({ feed: 'connecting', live: [] });

    unmount();
    expect(stop).toHaveBeenCalledTimes(2);
  });
});
