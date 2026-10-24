import type { OptimizationProgress } from '../model/useOptimizationProgress';
import { TrialsTable } from './TrialsTable';

const states: Record<OptimizationProgress['connection'], string> = {
  connecting: 'Подключение…',
  live: 'Испытания приходят по мере выполнения',
  reconnecting: 'Переподключение…',
  offline: 'Нет связи; страница покажет итог, когда оптимизация закончится',
};

/** The latest trials of a running optimization, newest first. */
export function OptimizationLive({
  progress,
  showWindow,
}: {
  progress: OptimizationProgress;
  showWindow: boolean;
}) {
  return (
    <section aria-label="Ход оптимизации" className="space-y-3">
      <p role="status" className="text-sm text-muted">
        {states[progress.connection]}
      </p>
      {progress.trials.length > 0 && (
        <TrialsTable trials={[...progress.trials].reverse()} showWindow={showWindow} />
      )}
    </section>
  );
}
