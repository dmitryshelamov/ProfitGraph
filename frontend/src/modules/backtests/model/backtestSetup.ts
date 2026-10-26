import type { InstrumentRules, SeriesSelection, Venue } from '@/modules/market-data';
import { utcDayStart } from '@/modules/market-data';
import { isPercentageParameter, type StrategyDefinition } from '@/modules/strategies';
import type { Schemas } from '@/shared/api/client';
import {
  fractionToPercent,
  isBackendDecimal,
  isDecimalString,
  multiplyDecimals,
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
  /**
   * Fees the seller pays per unit instead of the two rates, as the venue's marketplace charges them; null for rates. The
   * increment is the step of seller amounts in whole minor units as typed, null to follow the instrument's price tick.
   */
  readonly sellerFees: {
    readonly percents: readonly string[];
    readonly minimum: string;
    readonly increment: string | null;
  } | null;
  /** Days before a bought unit may be sold; null when the venue has no such hold, empty for none. */
  readonly holdDays: string | null;
}

export type BacktestSetupField =
  | 'from'
  | 'to'
  | 'startingCapital'
  | 'limitFeePercent'
  | 'marketFeePercent'
  | 'slippagePercent'
  | 'holdoutPercent'
  | 'sellerFeeMinimum'
  | 'sellerFeeIncrement'
  | 'holdDays'
  | `sellerFeePercents.${number}`
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
    sellerFees: null,
    holdDays: null,
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

/**
 * The venue's own execution defaults: a marketplace's seller fees and its hold, or the two rates and no hold. Kept
 * values the user typed for rates stay as they are.
 */
export function withVenueDefaults(
  values: BacktestSetupValues,
  venue: Venue | undefined,
): BacktestSetupValues {
  const defaults = venue?.executionDefaults ?? null;
  const fees = defaults?.sellerFees ?? null;
  const hold = defaults?.settlementHoldSeconds ?? null;

  return {
    ...values,
    sellerFees: fees
      ? {
          percents: fees.rates.map((rate) => fractionToPercent(rate) ?? rate),
          minimum: fees.minimum,
          increment: null,
        }
      : null,
    holdDays: hold === null ? null : String(hold / 86_400),
  };
}

/** The largest seller fee increment the API accepts, in minor units. */
const maxSellerFeeIncrement = 10_000;

/**
 * The step of seller amounts that the instrument's price tick sets, in whole minor units: a tick of 1.00 at two decimals
 * is 100, as on a Steam wallet that counts only whole tenge. Null without rules or for a tick finer than a minor unit.
 */
export function tickIncrement(rules: InstrumentRules | undefined): string | null {
  const units = rules
    ? multiplyDecimals(rules.priceTick, `1${'0'.repeat(rules.quotePrecision)}`)
    : null;

  return units !== null && /^[1-9]\d*$/.test(units) ? units : null;
}

/** A new symbol brings its own price tick, so an increment typed for the previous one follows the new tick instead. */
export function withSeries(
  values: BacktestSetupValues,
  series: SeriesSelection,
): BacktestSetupValues {
  const symbolChanged = series.symbol.trim() !== values.series.symbol.trim();

  return {
    ...values,
    series,
    sellerFees:
      values.sellerFees && symbolChanged
        ? { ...values.sellerFees, increment: null }
        : values.sellerFees,
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
 * become UTC midnight, and parameters marked as percent go back to fractions, so no value passes through number. A seller
 * fee increment nobody typed is the one the instrument's tick sets, given as `ruleIncrement`.
 */
export function backtestSetupRequest(
  strategyId: string,
  version: number,
  definition: StrategyDefinition,
  values: BacktestSetupValues,
  ruleIncrement: string | null = null,
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

  const limitFeeRate = values.sellerFees ? '0' : fraction(values.limitFeePercent, 50);
  const marketFeeRate = values.sellerFees ? '0' : fraction(values.marketFeePercent, 50);
  const sellerRates = values.sellerFees?.percents.map((percent) => fraction(percent, 50)) ?? [];
  const sellerMinimum = values.sellerFees ? exact(values.sellerFees.minimum) : null;

  sellerRates.forEach((rate, index) => {
    if (rate === null) {
      errors[`sellerFeePercents.${index}`] = 'От 0 до 50%.';
    }
  });

  if (values.sellerFees && (sellerMinimum === null || sellerMinimum.startsWith('-'))) {
    errors.sellerFeeMinimum = 'Укажите неотрицательную сумму.';
  }

  const incrementText = (values.sellerFees?.increment ?? ruleIncrement ?? '').trim();
  const sellerIncrement = /^\d{1,5}$/.test(incrementText) ? Number(incrementText) : null;

  if (
    values.sellerFees &&
    (sellerIncrement === null || sellerIncrement < 1 || sellerIncrement > maxSellerFeeIncrement)
  ) {
    errors.sellerFeeIncrement =
      incrementText === ''
        ? 'Укажите шаг: правила инструмента не прочитаны.'
        : `Целое число от 1 до ${maxSellerFeeIncrement}.`;
  }

  const holdText = values.holdDays?.trim() ?? '';
  const holdDays = /^\d{1,4}$/.test(holdText) ? Number(holdText) : null;

  if (holdText !== '' && (holdDays === null || holdDays > 3650)) {
    errors.holdDays = 'Целое число дней от 0 до 3650.';
  }
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
      ...(values.sellerFees
        ? {
            sellerFees: {
              rates: sellerRates as string[],
              minimum: sellerMinimum!,
              increment: sellerIncrement!,
            },
          }
        : {}),
      ...(holdDays === null ? {} : { settlementHoldSeconds: holdDays * 86_400 }),
      ...(values.series.credentialId ? { credentialId: values.series.credentialId } : {}),
    },
  };
}
