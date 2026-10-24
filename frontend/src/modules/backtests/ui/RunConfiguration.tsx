import type { ReactNode } from 'react';

import { timeframeLabel } from '@/modules/market-data';
import { parameterReadableName } from '@/modules/strategies';
import { fractionToPercent } from '@/shared/lib/decimal';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';

import type { BacktestRun } from '../model/types';

function percent(fraction: string): string {
  return `${fractionToPercent(fraction) ?? fraction}%`;
}

/** What the run froze when it started, and the history it was given. */
export function RunConfiguration({ run }: { run: BacktestRun }) {
  const configuration = run.configuration;
  const rows: [string, ReactNode][] = [
    ['Пара', `${configuration.symbol} · ${configuration.venue}`],
    ['Таймфрейм', timeframeLabel(configuration.timeframe)],
    [
      'Период',
      run.history ? (
        <>
          <DateTime value={run.history.from} /> — <DateTime value={run.history.to} />
        </>
      ) : (
        'определится при подготовке истории'
      ),
    ],
    [
      'Прогрев',
      run.history
        ? `${run.history.warmupCount} из ${configuration.warmupCandles} свечей`
        : `${configuration.warmupCandles} свечей`,
    ],
    ['Начальный капитал', configuration.startingCapital],
    [
      'Комиссии',
      `лимитные ${percent(configuration.limitFeeRate)}, рыночные ${percent(configuration.marketFeeRate)}`,
    ],
    ['Проскальзывание', percent(configuration.slippage)],
    ['Отложенная выборка', percent(configuration.outOfSampleFraction)],
    ...Object.entries(configuration.parameters).map(([name, value]): [string, ReactNode] => [
      parameterReadableName(name),
      <span className="font-mono">{value}</span>,
    ]),
  ];

  return (
    <Card title="Параметры запуска">
      <dl className="divide-y divide-line text-sm">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-baseline justify-between gap-4 py-2">
            <dt className="text-muted">{label}</dt>
            <dd className="text-right">{value}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
