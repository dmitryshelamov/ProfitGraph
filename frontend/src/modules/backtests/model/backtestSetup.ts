import type { SeriesSelection } from '@/modules/market-data';
import { utcDayStart } from '@/modules/market-data';
import { isPercentageParameter, type StrategyDefinition } from '@/modules/strategies';
import type { Schemas } from '@/shared/api/client';
import {
  fractionToPercent,
  isBackendDecimal,
  isDecimalString,
  percentToFraction,
} from '@/shared/lib/decimal';

export type StartBacktestRequest = Schemas['StartBacktestRequest'];

/** What the setup form edits; every number is the text the user typed, percents as percents. */
export interface BacktestSetupValues {
  readonly series: SeriesSelection;
  readonly from: string;
  readonly to: string;
  readonly startingCapital: string;
  readonly limitFeePercent: string;
  readonly marketFeePercent: string;
  readonly slippagePercent: string;
  readonly holdoutPercent: string;
  readonly parameters: Readonly<Record<string, string>>;
}

export type BacktestSetupField =
  | 'from'
  | 'to'
  | 'startingCapital'
  | 'limitFeePercent'
  | 'marketFeePercent'
  | 'slippagePercent'
  | 'holdoutPercent'
  | `parameters.${string}`;

export type BacktestSetupErrors = Partial<Record<BacktestSetupField, string>>;

/** Bybit spot fees and a 20% holdout; the strategy's own parameter values, percents shown as percents. */
export function backtestSetupDefaults(definition: StrategyDefinition): BacktestSetupValues {
  return {
    series: { venue: '', symbol: 'BTCUSDT', timeframe: '1h' },
    from: '',
    to: '',
    startingCapital: '1000',
    limitFeePercent: '0.1',
    marketFeePercent: '0.1',
    slippagePercent: '0',
    holdoutPercent: '20',
    parameters: Object.fromEntries(
      definition.parameters.map((parameter) => [
        parameter.name,
        isPercentageParameter(parameter.displayUnit)
          ? (fractionToPercent(parameter.defaultValue) ?? parameter.defaultValue)
          : parameter.defaultValue,
      ]),
    ),
  };
}

function exact(text: string): string | null {
  const value = text.trim();

  return isDecimalString(value) && isBackendDecimal(value) ? value : null;
}

/** A percent from 0 to a whole-number limit, inclusive, as the API's fraction. */
function fraction(text: string, limit: number): string | null {
  const value = exact(text);
  const result = value === null ? null : percentToFraction(value);

  return result !== null &&
    !value!.startsWith('-') &&
    isAtMost(value!, limit) &&
    isBackendDecimal(result)
    ? result
    : null;
}

/** Whether a nonnegative decimal string is at most a whole number, compared as text without floating point. */
function isAtMost(value: string, limit: number): boolean {
  const [integer = '0', decimals = ''] = value.split('.');
  const whole = BigInt(integer);
  const bound = BigInt(limit);

  return whole < bound || (whole === bound && /^0*$/.test(decimals));
}

/**
 * Turns the form into the exact request, or explains each invalid field. Percents become fractions as text, days
 * become UTC midnight, and parameters marked as percent go back to fractions, so no value passes through number.
 */
export function backtestSetupRequest(
  strategyId: string,
  version: number,
  definition: StrategyDefinition,
  values: BacktestSetupValues,
): { request: StartBacktestRequest } | { errors: BacktestSetupErrors } {
  const errors: BacktestSetupErrors = {};
  const from = values.from === '' ? null : utcDayStart(values.from);
  const to = values.to === '' ? null : utcDayStart(values.to);

  if (values.from !== '' && from === null) {
    errors.from = 'Укажите дату.';
  }

  if (values.to !== '' && to === null) {
    errors.to = 'Укажите дату.';
  }

  if (from !== null && to !== null && to <= from) {
    errors.to = 'Конец периода должен быть позже начала.';
  }

  const startingCapital = exact(values.startingCapital);

  if (
    startingCapital === null ||
    /^0(\.0*)?$/.test(startingCapital) ||
    startingCapital.startsWith('-')
  ) {
    errors.startingCapital = 'Укажите положительную сумму.';
  }

  const limitFeeRate = fraction(values.limitFeePercent, 50);
  const marketFeeRate = fraction(values.marketFeePercent, 50);
  const slippage = fraction(values.slippagePercent, 50);
  const outOfSampleFraction = fraction(values.holdoutPercent, 90);

  if (limitFeeRate === null) {
    errors.limitFeePercent = 'От 0 до 50%.';
  }

  if (marketFeeRate === null) {
    errors.marketFeePercent = 'От 0 до 50%.';
  }

  if (slippage === null) {
    errors.slippagePercent = 'От 0 до 50%.';
  }

  if (outOfSampleFraction === null) {
    errors.holdoutPercent = 'От 0 до 90%.';
  }

  const parameters: Record<string, string> = {};

  for (const parameter of definition.parameters) {
    const typed = exact(values.parameters[parameter.name] ?? '');
    const stored =
      typed !== null && isPercentageParameter(parameter.displayUnit)
        ? percentToFraction(typed)
        : typed;

    if (stored === null || !isBackendDecimal(stored)) {
      errors[`parameters.${parameter.name}`] = 'Укажите число.';
    } else {
      parameters[parameter.name] = stored;
    }
  }

  if (Object.keys(errors).length > 0) {
    return { errors };
  }

  return {
    request: {
      strategyId,
      version,
      venue: values.series.venue,
      symbol: values.series.symbol.trim(),
      timeframe: values.series.timeframe,
      from,
      to,
      parameters,
      startingCapital: startingCapital!,
      slippage: slippage!,
      limitFeeRate: limitFeeRate!,
      marketFeeRate: marketFeeRate!,
      outOfSampleFraction: outOfSampleFraction!,
    },
  };
}
