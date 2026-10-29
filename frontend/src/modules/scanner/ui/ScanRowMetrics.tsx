import { formatMetric } from '../model/format';
import { metricValue, scanMetrics } from '../model/metrics';
import type { ScanRow } from '../model/types';

/** Every metric of one pair from the latest snapshot, labelled as in the table. */
export function ScanRowMetrics({ row }: { row: ScanRow }) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm sm:grid-cols-3 lg:grid-cols-1">
      {scanMetrics.map((definition) => (
        <div key={definition.metric} className="flex justify-between gap-3">
          <dt className="text-text-secondary" title={definition.hint}>
            {definition.label}
          </dt>
          <dd className="font-mono">
            {formatMetric(metricValue(row, definition.metric), definition.places)}
          </dd>
        </div>
      ))}
    </dl>
  );
}
