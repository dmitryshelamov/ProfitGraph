import type { ChartLevel } from '@/modules/market-chart';

import type { ScanRow } from './types';

/** The pair's nearest support and resistance as dashed chart levels, from its snapshot row. */
export function rowLevels(row: ScanRow | undefined): ChartLevel[] {
  const levels: ChartLevel[] = [];

  if (row?.support) {
    levels.push({ id: 'support', side: 'Buy', price: row.support, title: 'Поддержка' });
  }

  if (row?.resistance) {
    levels.push({ id: 'resistance', side: 'Sell', price: row.resistance, title: 'Сопротивление' });
  }

  return levels;
}
