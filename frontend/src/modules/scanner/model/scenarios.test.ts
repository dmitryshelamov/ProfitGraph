import { scenarioLabel, scenarioTotals, tradeResult } from './scenarios';
import type { ScanSignal } from './types';

const trade = (scenario: string, resultR: string, direction = 'Long') => ({
  scenario,
  direction,
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
      direction: 'Long',
      trades: 2,
      wins: 1,
      totalR: '0.85',
    });
    expect(totals.find((total) => total.scenario === 'Pullback')?.totalR).toBeNull();
  });

  it('keeps shorts apart and shows them only once a signal traded short', () => {
    const signals = [signal([trade('Levels', '1.9'), trade('Levels', '-1', 'Short')])];
    const totals = scenarioTotals(signals);

    expect(totals).toHaveLength(10);
    expect(
      totals.find((total) => total.scenario === 'Levels' && total.direction === 'Short')?.totalR,
    ).toBe('-1');
    expect(tradeResult(signals[0]!, 'Levels')).toBe('1.9');
    expect(tradeResult(signals[0]!, 'Levels', 'Short')).toBe('-1');
    expect(scenarioTotals([signal([trade('Levels', '1.9')])])).toHaveLength(5);
  });

  it('counts a short beyond the four hours peak under its own scenario', () => {
    const totals = scenarioTotals([signal([trade('Extreme', '2', 'Short')])]);

    expect(
      totals.find((total) => total.scenario === 'Extreme' && total.direction === 'Short'),
    ).toEqual({ scenario: 'Extreme', direction: 'Short', trades: 1, wins: 1, totalR: '2' });
    expect(scenarioLabel('Extreme', 'Short')).toBe('Шорт, стоп за максимумом 4 часов, цель 2R');
  });

  it('reads a signal result per scenario', () => {
    const one = signal([trade('Levels', '1.9')]);

    expect(tradeResult(one, 'Levels')).toBe('1.9');
    expect(tradeResult(one, 'Pullback')).toBeNull();
  });
});
