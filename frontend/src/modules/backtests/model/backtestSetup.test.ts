import type { StrategyDefinition } from '@/modules/strategies';

import {
  backtestSetupDefaults,
  backtestSetupRequest,
  type BacktestSetupValues,
} from './backtestSetup';

const definition = {
  parameters: [
    { name: 'buyAmount', valueType: 'QuoteAmount', defaultValue: '100', displayUnit: 'quote' },
    { name: 'drop', valueType: 'Number', defaultValue: '0.015', displayUnit: 'percent' },
  ],
  values: [],
  blocks: [],
  wires: [],
} as unknown as StrategyDefinition;

function values(change: Partial<BacktestSetupValues> = {}): BacktestSetupValues {
  return {
    ...backtestSetupDefaults(definition),
    series: { venue: 'bybit', symbol: ' BTCUSDT ', timeframe: '1h' },
    ...change,
  };
}

describe('backtest setup', () => {
  it('starts from fees of Bybit spot, a 20% holdout and the strategy’s values with percents as percents', () => {
    const defaults = backtestSetupDefaults(definition);

    expect(defaults).toMatchObject({
      startingCapital: '1000',
      limitFeePercent: '0.1',
      marketFeePercent: '0.1',
      slippagePercent: '0',
      holdoutPercent: '20',
      parameters: { buyAmount: '100', drop: '1.5' },
    });
  });

  it('turns percents into exact fractions and days into UTC midnight', () => {
    const result = backtestSetupRequest(
      'strategy',
      3,
      definition,
      values({
        from: '2026-01-01',
        to: '2026-02-01',
        slippagePercent: '0.05',
        parameters: { buyAmount: '250.5', drop: '2.25' },
      }),
    );

    expect(result).toEqual({
      request: {
        strategyId: 'strategy',
        version: 3,
        venue: 'bybit',
        symbol: 'BTCUSDT',
        timeframe: '1h',
        from: '2026-01-01T00:00:00.000Z',
        to: '2026-02-01T00:00:00.000Z',
        parameters: { buyAmount: '250.5', drop: '0.0225' },
        startingCapital: '1000',
        slippage: '0.0005',
        limitFeeRate: '0.001',
        marketFeeRate: '0.001',
        outOfSampleFraction: '0.2',
      },
    });
  });

  it('leaves an open period to the venue’s first and latest closed candle', () => {
    const result = backtestSetupRequest('strategy', 1, definition, values());

    expect(result).toMatchObject({ request: { from: null, to: null } });
  });

  it('explains every invalid field instead of sending the request', () => {
    const result = backtestSetupRequest(
      'strategy',
      1,
      definition,
      values({
        from: '2026-02-01',
        to: '2026-01-01',
        startingCapital: '0',
        limitFeePercent: '50.5',
        marketFeePercent: '-1',
        slippagePercent: '1e-3',
        holdoutPercent: '95',
        parameters: { buyAmount: 'много', drop: '' },
      }),
    );

    expect(result).toEqual({
      errors: {
        to: 'Конец периода должен быть позже начала.',
        startingCapital: 'Укажите положительную сумму.',
        limitFeePercent: 'От 0 до 50%.',
        marketFeePercent: 'От 0 до 50%.',
        slippagePercent: 'От 0 до 50%.',
        holdoutPercent: 'От 0 до 90%.',
        'parameters.buyAmount': 'Укажите число.',
        'parameters.drop': 'Укажите число.',
      },
    });
  });

  it('accepts the inclusive limits', () => {
    const result = backtestSetupRequest(
      'strategy',
      1,
      definition,
      values({ limitFeePercent: '50', holdoutPercent: '90.00' }),
    );

    expect(result).toMatchObject({ request: { limitFeeRate: '0.5', outOfSampleFraction: '0.9' } });
  });
});
