import {
  type FormingCandle,
  formingNote,
  formingReducer,
  type FormingSnapshot,
  initialForming,
  liveCandles,
} from './formingCandle';

const candle = (openTime: string, close = '100', isClosed = false): FormingCandle => ({
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1m',
  openTime,
  open: '100',
  high: '110',
  low: '90',
  close,
  volume: '5',
  isClosed,
  observedAt: openTime,
});

const snapshot = (
  status: FormingSnapshot['status'],
  latest: FormingCandle | null = null,
): FormingSnapshot => ({
  venue: 'bybit',
  symbol: 'BTCUSDT',
  timeframe: '1m',
  status,
  candle: latest,
});

describe('forming candle state', () => {
  it('starts connecting with nothing to draw', () => {
    expect(initialForming.feed).toBe('connecting');
    expect(liveCandles(initialForming)).toEqual([]);
  });

  it('takes the feed state and the latest candle from the snapshot', () => {
    const state = formingReducer(initialForming, {
      type: 'snapshot',
      snapshot: snapshot('Live', candle('2026-10-26T09:00:00Z')),
    });

    expect(state.feed).toBe('live');
    expect(state.current?.openTime).toBe('2026-10-26T09:00:00Z');
  });

  it('updates the current candle in place while its interval lasts', () => {
    let state = formingReducer(initialForming, {
      type: 'candle',
      candle: candle('2026-10-26T09:00:00Z', '101'),
    });
    state = formingReducer(state, {
      type: 'candle',
      candle: candle('2026-10-26T09:00:00Z', '102'),
    });

    expect(liveCandles(state).map((item) => item.close)).toEqual(['102']);
    expect(state.feed).toBe('live');
  });

  it('keeps the candle before when the next interval starts, so the chart has no gap', () => {
    let state = formingReducer(initialForming, {
      type: 'candle',
      candle: candle('2026-10-26T09:00:00Z', '104', true),
    });
    state = formingReducer(state, {
      type: 'candle',
      candle: candle('2026-10-26T09:01:00Z', '105'),
    });

    expect(liveCandles(state).map((item) => item.openTime)).toEqual([
      '2026-10-26T09:00:00Z',
      '2026-10-26T09:01:00Z',
    ]);
  });

  it('ignores a candle older than the one shown and one with an unreadable time', () => {
    let state = formingReducer(initialForming, {
      type: 'candle',
      candle: candle('2026-10-26T09:01:00Z', '105'),
    });
    state = formingReducer(state, { type: 'candle', candle: candle('2026-10-26T09:00:00Z', '1') });
    state = formingReducer(state, { type: 'candle', candle: candle('not a time', '1') });

    expect(liveCandles(state).map((item) => item.close)).toEqual(['105']);
  });

  it('reports no data when the link or the feed is lost, and live again with the next candle', () => {
    let state = formingReducer(initialForming, {
      type: 'candle',
      candle: candle('2026-10-26T09:00:00Z'),
    });
    state = formingReducer(state, { type: 'lost' });

    expect(state.feed).toBe('noData');
    expect(state.current).not.toBeNull();

    state = formingReducer(state, { type: 'status', status: 'NoData' });
    expect(state.feed).toBe('noData');

    state = formingReducer(state, { type: 'candle', candle: candle('2026-10-26T09:00:00Z', '2') });
    expect(state.feed).toBe('live');
  });

  it('stays unsupported for a venue without a forming candle', () => {
    let state = formingReducer(initialForming, {
      type: 'snapshot',
      snapshot: snapshot('Unsupported'),
    });
    state = formingReducer(state, { type: 'lost' });

    expect(state.feed).toBe('unsupported');
    expect(formingNote(state.feed)).toBeNull();
  });

  it('starts over for another bot', () => {
    const state = formingReducer(
      formingReducer(initialForming, { type: 'candle', candle: candle('2026-10-26T09:00:00Z') }),
      { type: 'reset' },
    );

    expect(state).toEqual(initialForming);
  });

  it('says what the channel does', () => {
    expect(formingNote('live')).toContain('не закрыта');
    expect(formingNote('noData')).toContain('Нет данных');
    expect(formingNote('connecting')).toContain('Подключение');
  });
});
