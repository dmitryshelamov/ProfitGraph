import type { StrategyDefinition } from '@/modules/strategies';

import {
  optimizationSetupDefaults,
  optimizationSetupRequest,
  type OptimizationSetupValues,
} from './optimizationSetup';

const definition = {
  parameters: [
    { name: 'buyAmount', valueType: 'QuoteAmount', defaultValue: '100', displayUnit: 'quote' },
    { name: 'drop', valueType: 'Scalar', defaultValue: '0.015', displayUnit: 'percent' },
    { name: 'period', valueType: 'Integer', defaultValue: '20', displayUnit: null },
  ],
  values: [],
  blocks: [],
  wires: [],
} as unknown as StrategyDefinition;

function values(
  change: (current: OptimizationSetupValues) => Partial<OptimizationSetupValues> = () => ({}),
): OptimizationSetupValues {
  const defaults = optimizationSetupDefaults(definition);
  const base = {
    ...defaults,
    backtest: {
      ...defaults.backtest,
      series: { venue: 'bybit', symbol: 'BTCUSDT', timeframe: '1h' },
    },
  };

  return { ...base, ...change(base) };
}

function ranged(
  current: OptimizationSetupValues,
  name: string,
  min: string,
  max: string,
  step: string,
) {
  return { ...current.ranges, [name]: { enabled: true, min, max, step } };
}

describe('optimization setup', () => {
  it('starts with no range, the automatic method, a budget of 2000 and integer steps of one', () => {
    const defaults = optimizationSetupDefaults(definition);

    expect(defaults.method).toBe('auto');
    expect(defaults.budget).toBe('2000');
    expect(Object.values(defaults.ranges).every((range) => !range.enabled)).toBe(true);
    expect(defaults.ranges.period).toEqual({ enabled: false, min: '20', max: '20', step: '1' });
    expect(defaults.ranges.drop!.min).toBe('1.5');
  });

  it('needs at least one ranged parameter', () => {
    const result = optimizationSetupRequest('s', 2, definition, values());

    expect('errors' in result && result.errors.ranges).toBeTruthy();
  });

  it('sends ranges by name, percents as fractions, and leaves ranged parameters out of the fixed values', () => {
    const result = optimizationSetupRequest(
      's',
      2,
      definition,
      values((current) => ({
        ranges: {
          ...ranged(current, 'drop', '1', '3', '0.5'),
          period: { enabled: true, min: '10', max: '30', step: '5' },
        },
        method: 'grid',
        seed: '42',
      })),
    );

    expect('request' in result).toBe(true);

    if (!('request' in result)) {
      return;
    }

    expect(result.request.ranges).toEqual({
      drop: { min: '0.01', max: '0.03', step: '0.005' },
      period: { min: '10', max: '30', step: '5' },
    });
    expect(result.request.backtest!.parameters).toEqual({ buyAmount: '100' });
    expect(result.request).toMatchObject({
      method: 'grid',
      seed: '42',
      budget: 2000,
      finalistCount: 3,
      walkForward: null,
    });
  });

  it('explains a zero step, a reversed range and walk-forward limits', () => {
    const result = optimizationSetupRequest(
      's',
      2,
      definition,
      values((current) => ({
        ranges: {
          ...ranged(current, 'period', '30', '10', '1'),
          buyAmount: { enabled: true, min: '10', max: '20', step: '0' },
        },
        finalistCount: '3',
        walkForward: { enabled: true, windows: '1', trainToTestRatio: '25', scheme: 'rolling' },
      })),
    );

    expect('errors' in result).toBe(true);

    if (!('errors' in result)) {
      return;
    }

    expect(result.errors['ranges.period']).toBe('«До» не может быть меньше «от».');
    expect(result.errors['ranges.buyAmount']).toBe('Шаг должен быть больше нуля.');
    expect(result.errors.finalistCount).toBeTruthy();
    expect(result.errors['walkForward.windows']).toBeTruthy();
    expect(result.errors['walkForward.trainToTestRatio']).toBeTruthy();
  });

  it('sends walk-forward settings with one finalist', () => {
    const result = optimizationSetupRequest(
      's',
      2,
      definition,
      values((current) => ({
        ranges: ranged(current, 'period', '10', '30', '5'),
        finalistCount: '1',
        walkForward: { enabled: true, windows: '4', trainToTestRatio: '3', scheme: 'anchored' },
      })),
    );

    expect('request' in result && result.request.walkForward).toEqual({
      windows: 4,
      trainToTestRatio: 3,
      scheme: 'anchored',
    });
  });
});
