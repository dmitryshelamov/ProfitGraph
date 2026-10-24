import type { ReactNode } from 'react';

import { Amount } from '@/modules/backtests';
import { parameterReadableName } from '@/modules/strategies';

import { trialStatusLabel } from '../model/format';
import type { OptimizationTrial } from '../model/types';

/** Trials in a table: their values, how they ended and their in-sample result; `action` adds a first column. */
export function TrialsTable({
  trials,
  showWindow = false,
  showStage = false,
  action,
}: {
  trials: readonly OptimizationTrial[];
  showWindow?: boolean;
  showStage?: boolean;
  action?: (trial: OptimizationTrial) => ReactNode;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm" aria-label="Испытания">
        <thead>
          <tr className="text-left text-muted">
            {action && <th className="py-2 pr-4 font-normal" aria-label="Выбор" />}
            <th className="py-2 pr-4 font-normal">№</th>
            {showWindow && <th className="py-2 pr-4 font-normal">Окно</th>}
            {showStage && <th className="py-2 pr-4 font-normal">Этап</th>}
            <th className="py-2 pr-4 font-normal">Значения</th>
            <th className="py-2 pr-4 font-normal">Итог</th>
            <th className="py-2 pr-4 font-normal">Цель</th>
            <th className="py-2 pr-4 font-normal">Доходность, %</th>
            <th className="py-2 pr-4 font-normal">Прибыль</th>
            <th className="py-2 pr-4 font-normal">Просадка, %</th>
            <th className="py-2 pr-4 font-normal">Сделки</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {trials.map((trial) => (
            <tr key={trial.sequence}>
              {action && <td className="py-2 pr-4">{action(trial)}</td>}
              <td className="py-2 pr-4 font-mono">{trial.trialIndex + 1}</td>
              {showWindow && <td className="py-2 pr-4 font-mono">{trial.windowIndex + 1}</td>}
              {showStage && <td className="py-2 pr-4 font-mono">{trial.stage}</td>}
              <td className="py-2 pr-4">
                {Object.entries(trial.parameters)
                  .map(([name, value]) => `${parameterReadableName(name)} ${value}`)
                  .join(', ')}
              </td>
              <td className="py-2 pr-4">{trialStatusLabel(trial.status)}</td>
              <td className="py-2 pr-4">
                <Amount value={trial.objective} places={4} />
              </td>
              <td className="py-2 pr-4">
                <Amount value={trial.returnPercent} />
              </td>
              <td className="py-2 pr-4">
                <Amount value={trial.totalPnl} />
              </td>
              <td className="py-2 pr-4">
                <Amount value={trial.maxDrawdownPercent} />
              </td>
              <td className="py-2 pr-4 font-mono">{trial.executions}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
