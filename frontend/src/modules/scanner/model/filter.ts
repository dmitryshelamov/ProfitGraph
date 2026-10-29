import { compareDecimals } from '@/shared/lib/decimal';

import { metricValue, type ScanMetric } from './metrics';
import type { ScanRow } from './types';

export interface ScanConditionDraft {
  readonly metric: ScanMetric;
  /** Exact decimal strings; an empty bound is unset. */
  readonly minimum: string;
  readonly maximum: string;
}

export interface ScanView {
  readonly conditions: readonly ScanConditionDraft[];
  readonly sortMetric: ScanMetric;
  readonly sortDescending: boolean;
  readonly hideStale: boolean;
}

/**
 * The rows a view keeps, in its order: every condition holds (bounds inclusive, an empty metric fails), pairs without recent
 * trades are dropped when asked, and the sort puts empty values last. Values are compared exactly as decimal strings.
 */
export function applyView(rows: readonly ScanRow[], view: ScanView): ScanRow[] {
  const kept = rows.filter(
    (row) =>
      !(view.hideStale && row.stale) && view.conditions.every((condition) => holds(row, condition)),
  );

  return kept.sort((left, right) => {
    const a = metricValue(left, view.sortMetric);
    const b = metricValue(right, view.sortMetric);

    if (a === null || b === null) {
      return a === b ? left.symbol.localeCompare(right.symbol) : a === null ? 1 : -1;
    }

    const order = compareDecimals(a, b) ?? 0;

    return (view.sortDescending ? -order : order) || left.symbol.localeCompare(right.symbol);
  });
}

function holds(row: ScanRow, condition: ScanConditionDraft): boolean {
  const minimum = condition.minimum.trim();
  const maximum = condition.maximum.trim();

  if (minimum === '' && maximum === '') {
    return true;
  }

  const value = metricValue(row, condition.metric);

  if (value === null) {
    return false;
  }

  if (minimum !== '' && (compareDecimals(value, minimum) ?? -1) < 0) {
    return false;
  }

  return maximum === '' || (compareDecimals(value, maximum) ?? 1) <= 0;
}
