import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import type { OptimizationTrial } from '../model/types';

// Hub messages are not in the OpenAPI document; these mirror the API's OptimizationProgressResponse,
// OptimizationFinishedResponse and OptimizationCursorResponse.
interface ProgressMessage {
  readonly optimizationId: string;
  readonly trial: OptimizationTrial;
}

interface StatusMessage {
  readonly optimizationId: string;
  readonly status: string;
}

export interface OptimizationCursor {
  readonly lastSequence: number;
  readonly status: string;
}

export type OptimizationConnectionState = 'connecting' | 'live' | 'reconnecting' | 'offline';

export interface OptimizationProgressHandlers {
  readonly trial: (trial: OptimizationTrial) => void;
  readonly subscribed: (cursor: OptimizationCursor) => void;
  readonly status: (status: string) => void;
  readonly state: (state: OptimizationConnectionState) => void;
}

const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/**
 * Follows one optimization over /ws/optimizations. After every (re)connection it subscribes again and reports the
 * cursor, so the caller can read the trials it missed over HTTP. Returns the function that stops following.
 */
export function followOptimization(id: string, handlers: OptimizationProgressHandlers): () => void {
  const connection = new HubConnectionBuilder()
    .withUrl('/ws/optimizations')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;

  connection.on('OptimizationProgress', (message: ProgressMessage) => {
    if (same(message.optimizationId, id)) {
      handlers.trial(message.trial);
    }
  });

  for (const event of ['OptimizationPaused', 'OptimizationFinished']) {
    connection.on(event, (message: StatusMessage) => {
      if (same(message.optimizationId, id)) {
        handlers.status(message.status);
      }
    });
  }

  async function subscribe() {
    const cursor = await connection.invoke<OptimizationCursor>('Subscribe', id);

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
