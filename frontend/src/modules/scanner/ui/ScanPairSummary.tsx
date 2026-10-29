import { compareDecimals } from '@/shared/lib/decimal';

import { formatMetric } from '../model/format';
import { metricValue, type ScanMetric, scanMetrics } from '../model/metrics';
import type { ScanRow } from '../model/types';

/** The metrics a pair is judged by at a glance, in the page header; every metric stays in «Все метрики». */
const headline: readonly ScanMetric[] = [
  'Change1h',
  'Change24h',
  'VolumeSpike',
  'Natr24h',
  'Turnover24h',
  'Spread',
];

const changes: ReadonlySet<ScanMetric> = new Set<ScanMetric>(['Change1h', 'Change24h']);

/** A pair's headline metrics from the latest snapshot, the changes coloured by their sign. */
export function ScanPairSummary({ row }: { row: ScanRow }) {
  return (
    <dl aria-label="Главные метрики" className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
      {headline.map((metric) => {
        const definition = scanMetrics.find((item) => item.metric === metric)!;
        const value = metricValue(row, metric);
        const sign = value === null ? 0 : (compareDecimals(value, '0') ?? 0);
        const tone = !changes.has(metric) || sign === 0 ? '' : sign > 0 ? 'text-up' : 'text-down';

        return (
          <div key={metric} className="space-y-0.5">
            <dt className="text-xs text-text-secondary" title={definition.hint}>
              {definition.label}
            </dt>
            <dd className={`font-mono ${tone}`}>
              {changes.has(metric) && sign > 0 ? '+' : ''}
              {formatMetric(value, definition.places)}
            </dd>
          </div>
        );
      })}
    </dl>
  );
}
