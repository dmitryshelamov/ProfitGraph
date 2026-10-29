import type { ChartLevel } from '@/modules/market-chart';

import { tradeScenarioLabels } from './trades';
import type { ScanLevels, ScanTradeIdea } from './types';

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

/** An idea's entry, target and stop as solid chart lines: the entry in the neutral color, the target green and the stop red. */
export function ideaLevels(idea: ScanTradeIdea): ChartLevel[] {
  const name = `${tradeScenarioLabels[idea.scenario] ?? idea.scenario} ${idea.timeframe}`;
  const id = `idea:${idea.timeframe}:${idea.direction}:${idea.scenario}:${idea.entry}`;

  return [
    {
      id: `${id}:entry`,
      side: 'Buy',
      price: idea.entry,
      title: `Вход · ${name}`,
      solid: true,
      neutral: true,
    },
    { id: `${id}:target`, side: 'Buy', price: idea.target, title: 'Цель', solid: true },
    { id: `${id}:stop`, side: 'Sell', price: idea.stop, title: 'Стоп', solid: true },
  ];
}
