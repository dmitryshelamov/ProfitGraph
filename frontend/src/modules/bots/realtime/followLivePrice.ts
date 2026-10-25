import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import type { LivePrice } from '../model/types';

/**
 * Follows the display price of a bot's instrument over /ws/live-prices; after every reconnection it subscribes again.
 * A missed message is harmless: the position query reads the stored price. Returns the function that stops following.
 */
export function followLivePrice(botId: string, onPrice: (price: LivePrice) => void): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/live-prices')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;

  connection.on('LivePrice', (price: LivePrice) => {
    if (!stopped) {
      onPrice(price);
    }
  });
  connection.onreconnected(() => void connection.invoke('Subscribe', botId).catch(() => undefined));
  connection
    .start()
    .then(() => connection.invoke('Subscribe', botId))
    .catch(() => undefined);

  return () => {
    stopped = true;
    void connection.stop();
  };
}
