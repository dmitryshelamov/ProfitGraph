import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import type { BacktestObservation } from '../model/types';

// Hub messages are not in the OpenAPI document; these mirror the API's BacktestProgressResponse,
// BacktestFinishedResponse and BacktestCursorResponse.
interface ProgressMessage {
  readonly runId: string;
  readonly observation: BacktestObservation;
}

interface FinishedMessage {
  readonly runId: string;
  readonly outcome: string;
}

export interface RunCursor {
  readonly lastSequence: number;
  readonly outcome: string | null;
}

export type RunConnectionState = 'connecting' | 'live' | 'reconnecting' | 'offline';

export interface RunProgressHandlers {
  readonly progress: (observation: BacktestObservation) => void;
  readonly subscribed: (cursor: RunCursor) => void;
  readonly finished: (outcome: string) => void;
  readonly state: (state: RunConnectionState) => void;
}

const sameRun = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/**
 * Follows one run over /ws/backtests. After every (re)connection it subscribes again and reports the cursor, so the
 * caller can read what it missed over HTTP. Returns the function that stops following.
 */
export function followRun(runId: string, handlers: RunProgressHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/backtests')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;

  connection.on('BacktestProgress', (message: ProgressMessage) => {
    if (sameRun(message.runId, runId)) {
      handlers.progress(message.observation);
    }
  });
  connection.on('BacktestFinished', (message: FinishedMessage) => {
    if (sameRun(message.runId, runId)) {
      handlers.finished(message.outcome);
    }
  });

  async function subscribe() {
    const cursor = await connection.invoke<RunCursor>('Subscribe', runId);

    if (!stopped) {
      handlers.subscribed(cursor);
      handlers.state('live');
    }
  }

  connection.onreconnecting(() => handlers.state('reconnecting'));
  connection.onreconnected(() => subscribe().catch(() => handlers.state('offline')));
  connection.onclose(() => {
    if (!stopped) {
      handlers.state('offline');
    }
  });
  handlers.state('connecting');
  connection
    .start()
    .then(subscribe)
    .catch(() => {
      if (!stopped) {
        handlers.state('offline');
      }
    });

  return () => {
    stopped = true;
    void connection.stop();
  };
}
