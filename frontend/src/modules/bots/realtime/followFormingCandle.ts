import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import type { FormingCandle, FormingSnapshot, FormingStatusMessage } from '../model/formingCandle';

export interface FormingHandlers {
  onSnapshot: (snapshot: FormingSnapshot) => void;
  onCandle: (candle: FormingCandle) => void;
  onStatus: (status: FormingStatusMessage) => void;
  /** The link to the server is lost or being restored: nothing is arriving. */
  onLost: () => void;
}

/**
 * Follows the forming candle of a bot's series over /ws/forming-candles. Joining answers with the feed's state and its latest
 * candle; after every reconnection it joins again. Closing the page stops following, which lets the server close its venue
 * connection with the last follower. Returns the function that stops following.
 */
export function followFormingCandle(botId: string, handlers: FormingHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/forming-candles')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;

  const join = () =>
    connection
      .invoke<FormingSnapshot>('Subscribe', botId)
      .then((snapshot) => {
        if (!stopped) {
          handlers.onSnapshot(snapshot);
        }
      })
      .catch(() => {
        if (!stopped) {
          handlers.onLost();
        }
      });

  connection.on('FormingCandle', (candle: FormingCandle) => {
    if (!stopped) {
      handlers.onCandle(candle);
    }
  });
  connection.on('FormingStatus', (status: FormingStatusMessage) => {
    if (!stopped) {
      handlers.onStatus(status);
    }
  });
  connection.onreconnecting(() => {
    if (!stopped) {
      handlers.onLost();
    }
  });
  connection.onclose(() => {
    if (!stopped) {
      handlers.onLost();
    }
  });
  connection.onreconnected(() => void join());
  connection
    .start()
    .then(join)
    .catch(() => {
      if (!stopped) {
        handlers.onLost();
      }
    });

  return () => {
    stopped = true;
    void connection.stop();
  };
}
