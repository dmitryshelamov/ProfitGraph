import { fractionToPercent } from '@/shared/lib/decimal';

import { formatMetric } from '../model/format';
import type { ScanBacktest, ScanOutcomeSummary } from '../model/types';

type Summary = NonNullable<ScanBacktest['summary']>;

const rows: readonly {
  key: keyof Omit<Summary, 'signals' | 'scenarios' | 'calibration'>;
  label: string;
}[] = [
  { key: 'change15m', label: 'Через 15 мин' },
  { key: 'change1h', label: 'Через 1 ч' },
  { key: 'change4h', label: 'Через 4 ч' },
  { key: 'change24h', label: 'Через 24 ч' },
  { key: 'maxRise24h', label: 'Макс. рост за 24 ч' },
  { key: 'maxDrop24h', label: 'Макс. просадка за 24 ч' },
];

function percent(value: string | null): string {
  return value === null ? '—' : `${formatMetric(value, 2)}%`;
}

function share(outcome: ScanOutcomeSummary): string {
  const value = outcome.risingShare === null ? null : fractionToPercent(outcome.risingShare);

  return value === null ? '—' : `${formatMetric(value, 0)}%`;
}

/** What the price did after the backtest's signals: per horizon, how many signals reach it, how often it rose, the mean and median. */
export function ScanBacktestSummary({ summary }: { summary: Summary }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-right text-sm">
        <thead className="text-text-secondary">
          <tr>
            <th className="py-2 pr-3 text-left">Изменение цены</th>
            <th className="py-2 pl-3">Сигналов</th>
            <th className="py-2 pl-3">Доля роста</th>
            <th className="py-2 pl-3">Среднее</th>
            <th className="py-2 pl-3">Медиана</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ key, label }) => (
            <tr key={key} className="border-t border-line">
              <td className="py-1.5 pr-3 text-left">{label}</td>
              <td className="py-1.5 pl-3 font-mono">{summary[key].count}</td>
              <td className="py-1.5 pl-3 font-mono">{share(summary[key])}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(summary[key].mean)}</td>
              <td className="py-1.5 pl-3 font-mono">{percent(summary[key].median)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
