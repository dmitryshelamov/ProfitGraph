import { addDecimals } from '@/shared/lib/decimal';

import type { ScanSignal } from './types';

/** The trade scenarios of the preset backtest, in the order the page shows them (ADR 0059, section 8). */
export const scanScenarios = [
  { scenario: 'Levels', label: 'Уровни: стоп поддержка, цель сопротивление' },
  { scenario: 'LevelsRewardRisk2', label: 'Уровни, только R:R ≥ 2' },
  { scenario: 'Target2R', label: 'Стоп поддержка, цель 2R' },
  { scenario: 'Pullback', label: 'Откат 50% импульса, цель 2R' },
] as const;

export type ScanScenarioName = (typeof scanScenarios)[number]['scenario'];

export interface PairScenarioTotal {
  readonly scenario: ScanScenarioName;
  readonly trades: number;
  readonly wins: number;
  /** The exact sum of the trades' R, or null without trades. */
  readonly totalR: string | null;
}

/** Per scenario, the closed trades of the given signals: how many, how many won and their total R, summed exactly. */
export function scenarioTotals(signals: readonly ScanSignal[]): PairScenarioTotal[] {
  return scanScenarios.map(({ scenario }) => {
    const trades = signals.flatMap((signal) =>
      signal.trades.filter((trade) => trade.scenario === scenario),
    );

    return {
      scenario,
      trades: trades.length,
      wins: trades.filter((trade) => !trade.resultR.startsWith('-') && trade.resultR !== '0')
        .length,
      totalR: trades.reduce<string | null>(
        (sum, trade) => (sum === null ? trade.resultR : addDecimals(sum, trade.resultR)),
        null,
      ),
    };
  });
}

/** The result in R of a signal's trade in a scenario, or null when the scenario took no closed trade. */
export function tradeResult(signal: ScanSignal, scenario: ScanScenarioName): string | null {
  return signal.trades.find((trade) => trade.scenario === scenario)?.resultR ?? null;
}
