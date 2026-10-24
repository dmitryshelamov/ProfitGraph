import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { SequenceBuffer } from '@/shared/lib/sequenceBuffer';

import { optimizationKeys, optimizationTrials } from '../api/optimizationsApi';
import {
  followOptimization,
  type OptimizationConnectionState,
} from '../realtime/followOptimization';
import type { OptimizationTrial } from './types';

/** How many recent trials a live optimization shows; all of them are in the trials table once it finishes. */
export const LIVE_TRIAL_LIMIT = 200;

export interface OptimizationProgress {
  readonly trials: readonly OptimizationTrial[];
  readonly connection: OptimizationConnectionState;
}

const idle: OptimizationProgress = { trials: [], connection: 'offline' };

/**
 * Live trials of an optimization that has not finished: messages over SignalR, gaps filled from the stored trials
 * (stage 4 plan, S4-12). Every pause, resume or end reads the optimization and its windows again.
 */
export function useOptimizationProgress(id: string, enabled: boolean): OptimizationProgress {
  const queryClient = useQueryClient();
  const [progress, setProgress] = useState<OptimizationProgress>(idle);

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    const buffer = new SequenceBuffer<OptimizationTrial>(
      (after) => optimizationTrials(id, after),
      LIVE_TRIAL_LIMIT,
    );
    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: optimizationKeys.optimization(id) });
      void queryClient.invalidateQueries({ queryKey: optimizationKeys.windows(id) });
    };
    let refreshed = 0;
    const publish = () => {
      setProgress((current) => ({ ...current, trials: [...buffer.values] }));

      // Counters and the best trial change with every batch; read them at most once a second.
      if (Date.now() - refreshed >= 1000) {
        refreshed = Date.now();
        refresh();
      }
    };

    return followOptimization(id, {
      trial: (trial) => void buffer.receive(trial).then(publish, () => undefined),
      subscribed: (cursor) =>
        void buffer.resume(cursor.lastSequence, LIVE_TRIAL_LIMIT).then(publish, () => undefined),
      status: refresh,
      state: (connection) => setProgress((current) => ({ ...current, connection })),
    });
  }, [id, enabled, queryClient]);

  return enabled ? progress : idle;
}
