import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { backtestKeys, backtestObservations } from '../api/runsApi';
import { followRun, type RunConnectionState } from '../realtime/followRun';
import { ProgressBuffer } from './progressBuffer';
import type { BacktestObservation } from './types';

export interface RunProgress {
  readonly observations: readonly BacktestObservation[];
  readonly connection: RunConnectionState;
  readonly outcome: string | null;
}

const idle: RunProgress = { observations: [], connection: 'offline', outcome: null };

/**
 * Live observations of a run that has not finished: messages over SignalR, gaps filled from the stored observations.
 * When the run ends, its details, chart and facts are read again.
 */
export function useRunProgress(runId: string, enabled: boolean): RunProgress {
  const queryClient = useQueryClient();
  const [progress, setProgress] = useState<RunProgress>(idle);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const buffer = new ProgressBuffer((after) => backtestObservations(runId, after));
    const publish = () =>
      setProgress((current) => ({ ...current, observations: [...buffer.observations] }));
    const finish = (outcome: string) => {
      setProgress((current) => ({ ...current, outcome }));
      void queryClient.invalidateQueries({ queryKey: backtestKeys.run(runId) });
    };

    return followRun(runId, {
      progress: (observation) => void buffer.receive(observation).then(publish, () => undefined),
      subscribed: (cursor) => {
        void buffer.resume(cursor.lastSequence).then(publish, () => undefined);

        if (cursor.outcome) {
          finish(cursor.outcome);
        }
      },
      finished: finish,
      state: (connection) => setProgress((current) => ({ ...current, connection })),
    });
  }, [runId, enabled, queryClient]);

  return enabled ? progress : idle;
}
