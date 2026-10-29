import type { ReactNode } from 'react';

import { Card } from '@/shared/ui/Card';

import type { BacktestRun, SampleMetrics } from '../model/types';
import { Amount } from './Amount';

const columns: readonly [string, (metrics: SampleMetrics) => ReactNode][] = [
  ['Свечей', (metrics) => <span className="font-mono">{metrics.observedCandles}</span>],
  ['Прибыль', (metrics) => <Amount value={metrics.totalPnl} />],
  ['Доходность, %', (metrics) => <Amount value={metrics.returnPercent} />],
  ['Макс. просадка, %', (metrics) => <Amount value={metrics.maxDrawdownPercent} />],
  ['Комиссии в базовой', (metrics) => <Amount value={metrics.feesBase} places={8} />],
  ['Комиссии в котировке', (metrics) => <Amount value={metrics.feesQuote} />],
  ['Исполнений', (metrics) => <span className="font-mono">{metrics.executions}</span>],
  ['Макс. капитал в позиции', (metrics) => <Amount value={metrics.maxCapitalDeployed} />],
  ['Макс. задействовано с заявками', (metrics) => <Amount value={metrics.maxCapitalCommitted} />],
  ['В позиции, % свечей', (metrics) => <Amount value={metrics.timeInPositionPercent} />],
  ['Под водой, % свечей', (metrics) => <Amount value={metrics.timeUnderWaterPercent} />],
  [
    'Дольше всего под водой, свечей',
    (metrics) => <span className="font-mono">{metrics.longestUnderWaterCandles ?? '—'}</span>,
  ],
];

/**
 * The same metrics for the whole period, the in-sample part and the holdout at its end; a strategy tuned on the
 * in-sample part should hold up on the holdout. The capital columns show what the position cost besides fees (ADR 0057);
 * a run stored before them shows a dash.
 */
export function MetricsTable({ run }: { run: BacktestRun }) {
  const metrics = run.result?.metrics;

  if (!metrics) {
    return null;
  }

  const rows: [string, SampleMetrics | null][] = [
    ['Весь период', metrics.full],
    ['In-sample', metrics.inSample],
    ['Отложенная выборка', metrics.outOfSample],
  ];

  return (
    <Card title="Метрики">
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-muted">
              <th className="py-2 pr-4 font-normal">Выборка</th>
              {columns.map(([label]) => (
                <th key={label} className="py-2 pr-4 font-normal">
                  {label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map(([label, sample]) => (
              <tr key={label}>
                <th scope="row" className="py-2 pr-4 text-left font-normal">
                  {label}
                </th>
                {columns.map(([column, cell]) => (
                  <td key={column} className="py-2 pr-4">
                    {sample ? cell(sample) : '—'}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
