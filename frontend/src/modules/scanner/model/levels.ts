import type { ChartLevel } from '@/modules/market-chart';

import type { ScanLevels } from './types';

/** The pair's nearest support and resistance on the chart's timeframe as dashed chart levels. */
export function timeframeLevels(levels: ScanLevels | undefined): ChartLevel[] {
  const result: ChartLevel[] = [];

  if (levels?.support) {
    result.push({
      id: 'support',
      side: 'Buy',
      price: levels.support,
      title: `Поддержка ${levels.timeframe}`,
    });
  }

  if (levels?.resistance) {
    result.push({
      id: 'resistance',
      side: 'Sell',
      price: levels.resistance,
      title: `Сопротивление ${levels.timeframe}`,
    });
  }

  return result;
}
