import { fractionToPercent } from '@/shared/lib/decimal';

import { formatMetric } from '../model/format';
import { scanScenarios } from '../model/scenarios';
import type { ScanBacktest } from '../model/types';

type ScenarioSummary = NonNullable<ScanBacktest['summary']>['scenarios'][number];

function r(value: string | null): string {
  return value === null ? '—' : formatMetric(value, 2);
}

/**
 * How each trade scenario did over every signal (ADR 0059, section 8): closed trades, the share with a profit, mean, median and total
 * R after fees, the mean result in percent, the longest losing run and the mean time in a trade.
 */
export function ScanScenarioTable({ scenarios }: { scenarios: readonly ScenarioSummary[] }) {
  const rows = scanScenarios.flatMap(({ scenario, direction, label }) => {
    const summary = scenarios.find(
      (item) => item.scenario === scenario && (item.direction ?? 'Long') === direction,
    );

    return summary ? [{ label, summary }] : [];
  });

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-right text-sm">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2 pr-3 text-left">Сценарий</th>
            <th className="py-2 pl-3">Сделок</th>
            <th className="py-2 pl-3">Прибыльных</th>
            <th className="py-2 pl-3">Среднее, R</th>
            <th className="py-2 pl-3">Медиана, R</th>
            <th className="py-2 pl-3">Сумма, R</th>
            <th className="py-2 pl-3">Среднее, %</th>
            <th className="py-2 pl-3">Убытков подряд</th>
            <th className="py-2 pl-3">В сделке, мин</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ label, summary }) => {
            const share =
              summary.winningShare === null ? null : fractionToPercent(summary.winningShare);

            return (
              <tr key={`${summary.direction}:${summary.scenario}`} className="border-t border-line">
                <td className="py-1.5 pr-3 text-left">{label}</td>
                <td className="py-1.5 pl-3 font-mono">{summary.trades}</td>
                <td className="py-1.5 pl-3 font-mono">
                  {share === null ? '—' : `${formatMetric(share, 0)}%`}
                </td>
                <td className="py-1.5 pl-3 font-mono">{r(summary.meanR)}</td>
                <td className="py-1.5 pl-3 font-mono">{r(summary.medianR)}</td>
                <td className="py-1.5 pl-3 font-mono">{r(summary.totalR)}</td>
                <td className="py-1.5 pl-3 font-mono">{r(summary.meanPercent)}</td>
                <td className="py-1.5 pl-3 font-mono">{summary.longestLosingStreak}</td>
                <td className="py-1.5 pl-3 font-mono">
                  {summary.meanMinutes === null ? '—' : formatMetric(summary.meanMinutes, 0)}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
