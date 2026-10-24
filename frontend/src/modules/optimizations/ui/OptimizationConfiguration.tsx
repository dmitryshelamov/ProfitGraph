import { timeframeLabel } from '@/modules/market-data';
import { parameterReadableName } from '@/modules/strategies';
import { Card } from '@/shared/ui/Card';

import { methodLabel, objectiveLabel } from '../model/format';
import type { Optimization } from '../model/types';

/** What the optimization froze when it started. */
export function OptimizationConfiguration({ optimization }: { optimization: Optimization }) {
  const configuration = optimization.configuration;
  const backtest = configuration.backtest;
  const rows: [string, string][] = [
    ['Площадка', backtest.venue],
    ['Пара', `${backtest.symbol} · ${timeframeLabel(backtest.timeframe)}`],
    ['Метод', methodLabel(configuration.method)],
    ['Цель', objectiveLabel(configuration.objective)],
    [configuration.walkForward ? 'Испытаний на окно' : 'Испытаний', String(configuration.budget)],
    ['Зерно', configuration.seed],
    ['Сделок не меньше', String(configuration.minExecutions)],
    ['Просадка не больше, %', configuration.maxDrawdownPercent],
    ...Object.entries(configuration.ranges).map(([name, range]): [string, string] => [
      parameterReadableName(name),
      `${range.min} … ${range.max}, шаг ${range.step}`,
    ]),
    ...Object.entries(backtest.parameters).map(([name, value]): [string, string] => [
      parameterReadableName(name),
      value,
    ]),
  ];

  if (configuration.walkForward) {
    rows.push([
      'Окна',
      `${configuration.walkForward.windows}, обучение ×${configuration.walkForward.trainToTestRatio}, ${configuration.walkForward.scheme === 'anchored' ? 'растущее' : 'скользящее'}`,
    ]);
  }

  return (
    <Card title="Настройки">
      <dl className="space-y-2 text-sm">
        {rows.map(([label, value]) => (
          <div key={label} className="flex justify-between gap-3">
            <dt className="text-muted">{label}</dt>
            <dd className="text-right font-mono">{value}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
