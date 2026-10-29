import { scenarioTotals, tradeResult } from './scenarios';
import type { ScanSignal } from './types';

const trade = (scenario: string, resultR: string) => ({
  scenario,
  entry: '100',
  stop: '98',
  target: '104',
  enteredAt: '2026-10-29T10:00:00Z',
  exitedAt: '2026-10-29T11:00:00Z',
  exit: '104',
  reason: 'Target',
  resultR,
  resultPercent: '1',
});

const signal = (trades: ReturnType<typeof trade>[]) => ({ trades }) as unknown as ScanSignal;

describe('scenario totals', () => {
  it('sums each scenario exactly and counts wins', () => {
    const totals = scenarioTotals([
      signal([trade('Levels', '1.9'), trade('Target2R', '-1.1')]),
      signal([trade('Levels', '-1.05'), trade('Target2R', '1.95')]),
    ]);

    expect(totals.find((total) => total.scenario === 'Levels')).toEqual({
      scenario: 'Levels',
      trades: 2,
      wins: 1,
      totalR: '0.85',
    });
    expect(totals.find((total) => total.scenario === 'Pullback')?.totalR).toBeNull();
  });

  it('reads a signal result per scenario', () => {
    const one = signal([trade('Levels', '1.9')]);

    expect(tradeResult(one, 'Levels')).toBe('1.9');
    expect(tradeResult(one, 'Pullback')).toBeNull();
  });
});
