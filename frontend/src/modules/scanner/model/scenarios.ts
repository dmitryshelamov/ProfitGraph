import { addDecimals } from '@/shared/lib/decimal';

import type { ScanSignal } from './types';

/**
 * The trade scenarios of the preset backtest, in the order the page shows them (ADR 0059, section 8, and ADR 0072 for the stop beyond
 * the four hours' extreme); a venue with shorts adds their mirrors (ADR 0061).
 */
export const scanScenarios = [
  { scenario: 'Levels', direction: 'Long', label: 'Уровни: стоп поддержка, цель сопротивление' },
  { scenario: 'LevelsRewardRisk2', direction: 'Long', label: 'Уровни, только R:R ≥ 2' },
  { scenario: 'Target2R', direction: 'Long', label: 'Стоп поддержка, цель 2R' },
  { scenario: 'Pullback', direction: 'Long', label: 'Откат 50% импульса, цель 2R' },
  { scenario: 'Extreme', direction: 'Long', label: 'Стоп за минимумом 4 часов, цель 2R' },
  {
    scenario: 'Levels',
    direction: 'Short',
    label: 'Шорт, уровни: стоп сопротивление, цель поддержка',
  },
  { scenario: 'LevelsRewardRisk2', direction: 'Short', label: 'Шорт, уровни, только R:R ≥ 2' },
  { scenario: 'Target2R', direction: 'Short', label: 'Шорт, стоп сопротивление, цель 2R' },
  { scenario: 'Pullback', direction: 'Short', label: 'Шорт, откат 50% падения, цель 2R' },
  { scenario: 'Extreme', direction: 'Short', label: 'Шорт, стоп за максимумом 4 часов, цель 2R' },
] as const;

export type ScanScenarioItem = (typeof scanScenarios)[number];
export type ScanScenarioName = ScanScenarioItem['scenario'];
export type ScanDirection = ScanScenarioItem['direction'];

export interface PairScenarioTotal {
  readonly scenario: ScanScenarioName;
  readonly direction: ScanDirection;
  readonly trades: number;
  readonly wins: number;
  /** The exact sum of the trades' R, or null without trades. */
  readonly totalR: string | null;
}

type Trade = ScanSignal['trades'][number];

function matches(trade: Trade, scenario: ScanScenarioName, direction: ScanDirection): boolean {
  return trade.scenario === scenario && (trade.direction ?? 'Long') === direction;
}

/** The scenarios to show for the signals: the long ones, and the short ones once any signal traded short. */
export function shownScenarios(signals: readonly ScanSignal[]): readonly ScanScenarioItem[] {
  const shorts = signals.some((signal) =>
    signal.trades.some((trade) => trade.direction === 'Short'),
  );

  return scanScenarios.filter((item) => item.direction === 'Long' || shorts);
}

/** Per scenario, the closed trades of the given signals: how many, how many won and their total R, summed exactly. */
export function scenarioTotals(signals: readonly ScanSignal[]): PairScenarioTotal[] {
  return shownScenarios(signals).map(({ scenario, direction }) => {
    const trades = signals.flatMap((signal) =>
      signal.trades.filter((trade) => matches(trade, scenario, direction)),
    );

    return {
      scenario,
      direction,
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
export function tradeResult(
  signal: ScanSignal,
  scenario: ScanScenarioName,
  direction: ScanDirection = 'Long',
): string | null {
  return signal.trades.find((trade) => matches(trade, scenario, direction))?.resultR ?? null;
}

/** A scenario's label by its name and direction. */
export function scenarioLabel(scenario: string, direction: string): string | undefined {
  return scanScenarios.find((item) => item.scenario === scenario && item.direction === direction)
    ?.label;
}
