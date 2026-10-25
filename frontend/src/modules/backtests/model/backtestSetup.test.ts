import type { Venue } from '@/modules/market-data';
import type { StrategyDefinition } from '@/modules/strategies';

import {
  backtestSetupDefaults,
  backtestSetupRequest,
  type BacktestSetupValues,
  withVenueDefaults,
} from './backtestSetup';

const marketplace: Venue = {
  id: 'steam',
  name: 'Steam Community Market',
  timeframes: ['1h', '1d'],
  executionDefaults: {
    sellerFees: { rates: ['0.05', '0.1'], minimum: '0.01' },
    settlementHoldSeconds: 604_800,
  },
};

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

  it('takes a marketplace venue’s seller fees and hold, and drops them for a venue without', () => {
    const steam = withVenueDefaults(values(), marketplace);

    expect(steam.sellerFees).toEqual({ percents: ['5', '10'], minimum: '0.01' });
    expect(steam.holdDays).toBe('7');
    expect(
      withVenueDefaults(steam, {
        id: 'bybit',
        name: 'Bybit',
        timeframes: ['1h'],
        executionDefaults: null,
      }),
    ).toMatchObject({
      sellerFees: null,
      holdDays: null,
    });
  });

  it('sends seller fees instead of the rates and the hold in seconds', () => {
    const result = backtestSetupRequest('s', 1, definition, {
      ...withVenueDefaults(values(), marketplace),
      series: { venue: 'steam', symbol: 'KZT:730:Dreams & Nightmares Case', timeframe: '1d' },
    });

    expect(result).toMatchObject({
      request: {
        symbol: 'KZT:730:Dreams & Nightmares Case',
        limitFeeRate: '0',
        marketFeeRate: '0',
        sellerFees: { rates: ['0.05', '0.1'], minimum: '0.01' },
        settlementHoldSeconds: 604_800,
      },
    });
  });

  it('explains a seller fee, minimum or hold it cannot send', () => {
    const steam = withVenueDefaults(values(), marketplace);
    const result = backtestSetupRequest('s', 1, definition, {
      ...steam,
      sellerFees: { percents: ['5', '60'], minimum: '-1' },
      holdDays: '1.5',
    });

    expect(result).toEqual({
      errors: {
        'sellerFeePercents.1': 'От 0 до 50%.',
        sellerFeeMinimum: 'Укажите неотрицательную сумму.',
        holdDays: 'Целое число дней от 0 до 3650.',
      },
    });
  });

  it('sends no hold when its field is left empty', () => {
    const result = backtestSetupRequest('s', 1, definition, {
      ...withVenueDefaults(values(), marketplace),
      holdDays: '',
    });

    expect(result).toHaveProperty('request');
    expect(result).not.toHaveProperty('request.settlementHoldSeconds');
  });
});
