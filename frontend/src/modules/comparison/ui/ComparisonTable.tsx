import type { ReactNode } from 'react';

/** One column of the comparison: a run or a trial, with its values and metrics as exact decimal strings. */
export interface ComparisonColumn {
  readonly key: string;
  readonly title: ReactNode;
  readonly subtitle: string;
  readonly parameters: Readonly<Record<string, string>>;
  readonly metrics: Readonly<Partial<Record<ComparisonMetric, string | number | null>>>;
}

export type ComparisonMetric =
  | 'fullReturn'
  | 'fullPnl'
  | 'fullDrawdown'
  | 'fullExecutions'
  | 'inSampleReturn'
  | 'inSamplePnl'
  | 'inSampleDrawdown'
  | 'inSampleExecutions'
  | 'outOfSampleReturn'
  | 'outOfSampleDrawdown'
  | 'objective';

const rows: readonly [ComparisonMetric, string][] = [
  ['fullReturn', 'Весь период: доходность, %'],
  ['fullPnl', 'Весь период: прибыль'],
  ['fullDrawdown', 'Весь период: просадка, %'],
  ['fullExecutions', 'Весь период: сделки'],
  ['inSampleReturn', 'Обучающая часть: доходность, %'],
  ['inSamplePnl', 'Обучающая часть: прибыль'],
  ['inSampleDrawdown', 'Обучающая часть: просадка, %'],
  ['inSampleExecutions', 'Обучающая часть: сделки'],
  ['outOfSampleReturn', 'Отложенная выборка: доходность, %'],
  ['outOfSampleDrawdown', 'Отложенная выборка: просадка, %'],
  ['objective', 'Цель испытания'],
];

/**
 * Runs and trials side by side: their parameter values and their metrics for the whole period, the in-sample part and
 * the holdout. A trial has only in-sample metrics; a missing value shows a dash.
 */
export function ComparisonTable({
  columns,
  readableName,
  format,
}: {
  columns: readonly ComparisonColumn[];
  readableName: (parameter: string) => string;
  format: (value: string | number | null | undefined) => ReactNode;
}) {
  const parameters = [
    ...new Set(columns.flatMap((column) => Object.keys(column.parameters))),
  ].sort();
  const metrics = rows.filter(([metric]) =>
    columns.some((column) => column.metrics[metric] !== undefined),
  );

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm" aria-label="Сравнение">
        <thead>
          <tr className="text-left align-top">
            <th className="py-2 pr-4 font-normal text-muted" />
            {columns.map((column) => (
              <th key={column.key} scope="col" className="py-2 pr-4 font-medium">
                <div>{column.title}</div>
                <div className="font-normal text-muted">{column.subtitle}</div>
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {parameters.map((parameter) => (
            <tr key={`parameter:${parameter}`}>
              <th scope="row" className="py-2 pr-4 text-left font-normal text-muted">
                {readableName(parameter)}
              </th>
              {columns.map((column) => (
                <td key={column.key} className="py-2 pr-4 font-mono">
                  {column.parameters[parameter] ?? '—'}
                </td>
              ))}
            </tr>
          ))}
          {metrics.map(([metric, label]) => (
            <tr key={metric}>
              <th scope="row" className="py-2 pr-4 text-left font-normal text-muted">
                {label}
              </th>
              {columns.map((column) => (
                <td key={column.key} className="py-2 pr-4">
                  {format(column.metrics[metric])}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
