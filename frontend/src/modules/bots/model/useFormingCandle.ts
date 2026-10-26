import { useEffect, useMemo, useReducer } from 'react';

import { followFormingCandle } from '../realtime/followFormingCandle';
import {
  type FormingCandle,
  type FormingFeedState,
  formingReducer,
  initialForming,
  liveCandles,
} from './formingCandle';

/**
 * The bot's forming candle and the state of its channel. The channel opens with the page and closes with it; a lost link shows
 * as no data and is restored by the connection itself, so the chart never depends on it.
 */
export function useFormingCandle(botId: string): { feed: FormingFeedState; live: FormingCandle[] } {
  const [state, dispatch] = useReducer(formingReducer, initialForming);

  useEffect(() => {
    dispatch({ type: 'reset' });

    return followFormingCandle(botId, {
      onSnapshot: (snapshot) => dispatch({ type: 'snapshot', snapshot }),
      onCandle: (candle) => dispatch({ type: 'candle', candle }),
      onStatus: (message) => dispatch({ type: 'status', status: message.status }),
      onLost: () => dispatch({ type: 'lost' }),
    });
  }, [botId]);

  const live = useMemo(() => liveCandles(state), [state]);

  return { feed: state.feed, live };
}
