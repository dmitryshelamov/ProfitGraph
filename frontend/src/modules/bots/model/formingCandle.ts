/**
 * The forming candle of a bot's series as the `/ws/forming-candles` hub sends it (ADR 0026): not in the HTTP schema, so the
 * messages are typed here. Prices are exact decimal strings; the candle lives in memory only and the closed history of the chart
 * stays the source of truth.
 */
export interface FormingCandle {
  readonly venue: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly openTime: string;
  readonly open: string;
  readonly high: string;
  readonly low: string;
  readonly close: string;
  readonly volume: string;
  readonly isClosed: boolean;
  readonly observedAt: string;
}

export type FormingStatus = 'Live' | 'NoData' | 'Unsupported';

/** A `FormingStatus` hub message. */
export interface FormingStatusMessage {
  readonly venue: string;
  readonly symbol: string;
  readonly timeframe: string;
  readonly status: FormingStatus;
}

/** What joining the feed returns: its state and the latest candle it holds. */
export interface FormingSnapshot extends FormingStatusMessage {
  readonly candle: FormingCandle | null;
}

/** What the page shows about the channel: waiting for the first answer, delivering, silent, or not offered. */
export type FormingFeedState = 'connecting' | 'live' | 'noData' | 'unsupported';

export interface FormingState {
  readonly feed: FormingFeedState;
  /** The candle being formed, or the newest one received. */
  readonly current: FormingCandle | null;
  /** The candle before it, kept so the chart shows no gap until the history has the closed candle. */
  readonly completed: FormingCandle | null;
}

export type FormingEvent =
  | { readonly type: 'snapshot'; readonly snapshot: FormingSnapshot }
  | { readonly type: 'candle'; readonly candle: FormingCandle }
  | { readonly type: 'status'; readonly status: FormingStatus }
  | { readonly type: 'lost' }
  | { readonly type: 'reset' };

export const initialForming: FormingState = { feed: 'connecting', current: null, completed: null };

const feedOf: Readonly<Record<FormingStatus, FormingFeedState>> = {
  Live: 'live',
  NoData: 'noData',
  Unsupported: 'unsupported',
};

function withCandle(state: FormingState, candle: FormingCandle): FormingState {
  const at = Date.parse(candle.openTime);
  const current = state.current;

  if (!Number.isFinite(at)) {
    return state;
  }

  if (current === null) {
    return { ...state, current: candle };
  }

  const known = Date.parse(current.openTime);

  if (at < known) {
    return state;
  }

  // A candle of the next interval ends the current one; the same interval just updates it.
  return at > known
    ? { ...state, completed: current, current: candle }
    : { ...state, current: candle };
}

/** Applies one hub event: an older candle than the one shown is ignored, a newer interval keeps the one before it. */
export function formingReducer(state: FormingState, event: FormingEvent): FormingState {
  switch (event.type) {
    case 'snapshot': {
      const next = { ...state, feed: feedOf[event.snapshot.status] };

      return event.snapshot.candle ? withCandle(next, event.snapshot.candle) : next;
    }
    case 'candle':
      return { ...withCandle(state, event.candle), feed: 'live' };
    case 'status':
      return { ...state, feed: feedOf[event.status] };
    case 'lost':
      return state.feed === 'unsupported' ? state : { ...state, feed: 'noData' };
    case 'reset':
      return initialForming;
  }
}

/** The candles to draw on top of the history, oldest first. */
export function liveCandles(state: FormingState): FormingCandle[] {
  return [state.completed, state.current].filter((candle) => candle !== null);
}

/** The line under the chart about the channel; nothing for a venue that has no forming candle. */
export function formingNote(feed: FormingFeedState): string | null {
  switch (feed) {
    case 'connecting':
      return 'Подключение к потоку формирующейся свечи…';
    case 'live':
      return 'Последняя свеча формируется и обновляется в реальном времени, она ещё не закрыта.';
    case 'noData':
      return 'Нет данных по формирующейся свече: поток площадки недоступен. График показывает закрытые свечи.';
    case 'unsupported':
      return null;
  }
}
