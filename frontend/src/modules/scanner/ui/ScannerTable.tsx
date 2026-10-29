import { formatMetric } from '../model/format';
import { metricValue, type ScanMetric, scanMetrics } from '../model/metrics';
import type { ScanRow } from '../model/types';

/** The scanner's rows; a header click sorts by that metric, a second click turns the order around. */
export function ScannerTable({
  rows,
  sortMetric,
  sortDescending,
  onSort,
}: {
  rows: readonly ScanRow[];
  sortMetric: ScanMetric;
  sortDescending: boolean;
  onSort: (metric: ScanMetric) => void;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-right text-sm">
        <thead className="text-text-secondary">
          <tr>
            <th className="sticky left-0 bg-panel py-2 pr-3 text-left">Пара</th>
            {scanMetrics.map((definition) => (
              <th
                key={definition.metric}
                className="py-2 pl-3 whitespace-nowrap"
                aria-sort={
                  definition.metric === sortMetric
                    ? sortDescending
                      ? 'descending'
                      : 'ascending'
                    : undefined
                }
              >
                <button
                  type="button"
                  title={definition.hint}
                  className="hover:text-text"
                  onClick={() => onSort(definition.metric)}
                >
                  {definition.label}
                  {definition.metric === sortMetric ? (sortDescending ? ' ↓' : ' ↑') : ''}
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.symbol} className="border-t border-line">
              <td className="sticky left-0 bg-panel py-1.5 pr-3 text-left whitespace-nowrap">
                <span className="font-medium">{row.symbol}</span>
                {row.stale && (
                  <span className="ml-2 text-xs text-muted" title="Сделок не было больше 15 минут">
                    нет сделок
                  </span>
                )}
                {row.incomplete && (
                  <span
                    className="ml-2 text-xs text-warning-text"
                    title="Свечи догружаются после обрыва связи"
                  >
                    неполные данные
                  </span>
                )}
              </td>
              {scanMetrics.map((definition) => (
                <td key={definition.metric} className="py-1.5 pl-3 font-mono whitespace-nowrap">
                  {formatMetric(metricValue(row, definition.metric), definition.places)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
