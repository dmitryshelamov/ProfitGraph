import type { Venue } from '@/modules/market-data';
import type { StrategyDefinition } from '@/modules/strategies';

import {
  backtestSetupDefaults,
  backtestSetupRequest,
  type BacktestSetupValues,
  tickIncrement,
  withSeries,
  withVenueDefaults,
} from './backtestSetup';

const marketplace: Venue = {
  id: 'steam',
  name: 'Steam Community Market',
  timeframes: ['1h', '1d'],
  executionDefaults: {
    sellerFees: { rates: ['0.05', '0.1'], minimum: '0.01', increment: 1 },
    settlementHoldSeconds: 604_800,
  },
  botSettings: null,
  backtestLimits: [],
  credentialVenue: null,
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
        limitFill: 'Through',
      },
    });
  });

  it('keeps a fill on touch when the user picks it', () => {
    const result = backtestSetupRequest('strategy', 1, definition, values({ limitFill: 'Touch' }));

    expect(result).toMatchObject({ request: { limitFill: 'Touch' } });
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

    expect(steam.sellerFees).toEqual({ percents: ['5', '10'], minimum: '0.01', increment: null });
    expect(steam.holdDays).toBe('7');
    expect(
      withVenueDefaults(steam, {
        id: 'bybit',
        name: 'Bybit',
        timeframes: ['1h'],
        executionDefaults: null,
        botSettings: null,
        backtestLimits: [],
        credentialVenue: null,
      }),
    ).toMatchObject({
      sellerFees: null,
      holdDays: null,
    });
  });

  it('sends seller fees instead of the rates, in the increment of the instrument’s tick, and the hold in seconds', () => {
    const result = backtestSetupRequest(
      's',
      1,
      definition,
      {
        ...withVenueDefaults(values(), marketplace),
        series: { venue: 'steam', symbol: 'KZT:730:Dreams & Nightmares Case', timeframe: '1d' },
      },
      '100',
    );

    expect(result).toMatchObject({
      request: {
        symbol: 'KZT:730:Dreams & Nightmares Case',
        limitFeeRate: '0',
        marketFeeRate: '0',
        sellerFees: { rates: ['0.05', '0.1'], minimum: '0.01', increment: 100 },
        settlementHoldSeconds: 604_800,
      },
    });
  });

  it('sends a typed seller fee increment over the tick’s and asks for one without either', () => {
    const steam = withVenueDefaults(values(), marketplace);
    const typed = { ...steam, sellerFees: { ...steam.sellerFees!, increment: ' 1 ' } };

    expect(backtestSetupRequest('s', 1, definition, typed, '100')).toMatchObject({
      request: { sellerFees: { increment: 1 } },
    });
    expect(backtestSetupRequest('s', 1, definition, steam)).toEqual({
      errors: { sellerFeeIncrement: 'Укажите шаг: правила инструмента не прочитаны.' },
    });
  });

  it.each(['0', '10001', '1.5', '-1', 'сто'])(
    'rejects the seller fee increment %s',
    (increment) => {
      const steam = withVenueDefaults(values(), marketplace);

      expect(
        backtestSetupRequest('s', 1, definition, {
          ...steam,
          sellerFees: { ...steam.sellerFees!, increment },
        }),
      ).toEqual({ errors: { sellerFeeIncrement: 'Целое число от 1 до 10000.' } });
    },
  );

  it('takes the increment from the tick in minor units, and none from a tick finer than one', () => {
    const rules = {
      quantityStep: '1',
      priceTick: '1.00',
      minQuantity: '1',
      minNotional: '1.15',
      basePrecision: 0,
      quotePrecision: 2,
    };

    expect(tickIncrement(rules)).toBe('100');
    expect(tickIncrement({ ...rules, priceTick: '0.01' })).toBe('1');
    expect(tickIncrement({ ...rules, priceTick: '0.001' })).toBeNull();
    expect(tickIncrement(undefined)).toBeNull();
  });

  it('lets a new symbol’s tick replace a typed increment, and keeps it for the same symbol', () => {
    const steam = withVenueDefaults(values(), marketplace);
    const typed = { ...steam, sellerFees: { ...steam.sellerFees!, increment: '100' } };

    expect(withSeries(typed, { ...typed.series, timeframe: '1d' }).sellerFees?.increment).toBe(
      '100',
    );
    expect(
      withSeries(typed, { ...typed.series, symbol: 'KZT:730:Kilowatt Case' }).sellerFees?.increment,
    ).toBeNull();
    expect(withSeries(values(), { ...values().series, symbol: 'ETHUSDT' }).sellerFees).toBeNull();
  });

  it('explains a seller fee, minimum or hold it cannot send', () => {
    const steam = withVenueDefaults(values(), marketplace);
    const result = backtestSetupRequest('s', 1, definition, {
      ...steam,
      sellerFees: { percents: ['5', '60'], minimum: '-1', increment: '1' },
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
    const result = backtestSetupRequest(
      's',
      1,
      definition,
      { ...withVenueDefaults(values(), marketplace), holdDays: '' },
      '1',
    );

    expect(result).toHaveProperty('request');
    expect(result).not.toHaveProperty('request.settlementHoldSeconds');
  });
});
