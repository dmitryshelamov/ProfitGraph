import {
  backtestSetupDefaults,
  type BacktestSetupErrors,
  backtestSetupRequest,
  type BacktestSetupValues,
} from '@/modules/backtests';
import { isPercentageParameter, type StrategyDefinition } from '@/modules/strategies';
import {
  compareDecimals,
  isBackendDecimal,
  isDecimalString,
  percentToFraction,
} from '@/shared/lib/decimal';

import type { StartOptimizationRequest } from './types';

export type OptimizationMethod = 'auto' | 'grid' | 'random' | 'coarse_to_fine' | 'tpe';
export type OptimizationObjective =
  'in_sample_return_percent' | 'in_sample_total_pnl' | 'in_sample_return_to_drawdown';

/** One parameter's range as typed; a parameter without a range keeps the backtest's fixed value. */
export interface ParameterRange {
  readonly enabled: boolean;
  readonly min: string;
  readonly max: string;
  readonly step: string;
}

/** What the optimization form edits: the backtest it searches around, the ranges and how the search runs. */
export interface OptimizationSetupValues {
  readonly backtest: BacktestSetupValues;
  readonly ranges: Readonly<Record<string, ParameterRange>>;
  readonly method: OptimizationMethod;
  readonly objective: OptimizationObjective;
  readonly budget: string;
  readonly seed: string;
  readonly finalistCount: string;
  readonly minExecutions: string;
  readonly maxDrawdownPercent: string;
  readonly walkForward: {
    readonly enabled: boolean;
    readonly windows: string;
    readonly trainToTestRatio: string;
    readonly scheme: 'rolling' | 'anchored';
  };
}

export type OptimizationSetupField =
  | 'ranges'
  | 'budget'
  | 'seed'
  | 'finalistCount'
  | 'minExecutions'
  | 'maxDrawdownPercent'
  | 'walkForward.windows'
  | 'walkForward.trainToTestRatio'
  | `ranges.${string}`;

export type OptimizationSetupErrors = Partial<Record<OptimizationSetupField, string>> & {
  readonly backtest?: BacktestSetupErrors;
};

/** The backtest defaults, no range yet, automatic method, a budget of 2000 and three finalists. */
export function optimizationSetupDefaults(definition: StrategyDefinition): OptimizationSetupValues {
  const backtest = backtestSetupDefaults(definition);

  return {
    backtest,
    ranges: Object.fromEntries(
      definition.parameters.map((parameter) => {
        const value = backtest.parameters[parameter.name] ?? '';

        return [
          parameter.name,
          {
            enabled: false,
            min: value,
            max: value,
            step: parameter.valueType === 'Integer' ? '1' : value,
          },
        ];
      }),
    ),
    method: 'auto',
    objective: 'in_sample_return_percent',
    budget: '2000',
    seed: '',
    finalistCount: '3',
    minExecutions: '1',
    maxDrawdownPercent: '50',
    walkForward: { enabled: false, windows: '6', trainToTestRatio: '4', scheme: 'rolling' },
  };
}

/** A whole number within bounds, typed as digits only. */
function whole(text: string, min: number, max: number): number | null {
  const value = text.trim();

  if (!/^\d{1,7}$/.test(value)) {
    return null;
  }

  const parsed = Number(value);

  return parsed >= min && parsed <= max ? parsed : null;
}

function exact(text: string): string | null {
  const value = text.trim();

  return isDecimalString(value) && isBackendDecimal(value) ? value : null;
}

/**
 * Turns the form into the exact request, or explains each invalid field. Ranges of parameters shown as percents go
 * back to fractions as text; ranged parameters are left out of the backtest's fixed values.
 */
export function optimizationSetupRequest(
  strategyId: string,
  version: number,
  definition: StrategyDefinition,
  values: OptimizationSetupValues,
): { request: StartOptimizationRequest } | { errors: OptimizationSetupErrors } {
  const errors: Partial<Record<OptimizationSetupField, string>> = {};
  const ranged = definition.parameters.filter(
    (parameter) => values.ranges[parameter.name]?.enabled,
  );
  const fixed = {
    ...definition,
    parameters: definition.parameters.filter(
      (parameter) => !values.ranges[parameter.name]?.enabled,
    ),
  };
  const backtest = backtestSetupRequest(strategyId, version, fixed, values.backtest);
  const ranges: Record<string, { min: string; max: string; step: string }> = {};

  if (ranged.length === 0) {
    errors.ranges = 'Выберите хотя бы один параметр для поиска.';
  }

  for (const parameter of ranged) {
    const range = values.ranges[parameter.name]!;
    const percent = isPercentageParameter(parameter.displayUnit);
    const [min, max, step] = [range.min, range.max, range.step].map((text) => {
      const typed = exact(text);
      const stored = typed !== null && percent ? percentToFraction(typed) : typed;

      return stored !== null && isBackendDecimal(stored) ? stored : null;
    });

    if (min == null || max == null || step == null) {
      errors[`ranges.${parameter.name}`] = 'Укажите числа: от, до и шаг.';
    } else if (compareDecimals(step, '0')! <= 0) {
      errors[`ranges.${parameter.name}`] = 'Шаг должен быть больше нуля.';
    } else if (compareDecimals(max, min)! < 0) {
      errors[`ranges.${parameter.name}`] = '«До» не может быть меньше «от».';
    } else {
      ranges[parameter.name] = { min, max, step };
    }
  }

  const budget = whole(values.budget, 1, 1_000_000);
  const walkForward = values.walkForward.enabled;
  const finalistCount = whole(values.finalistCount, 0, walkForward ? 1 : 10);
  const minExecutions = whole(values.minExecutions, 0, 1_000_000);
  const maxDrawdown = exact(values.maxDrawdownPercent);
  const seed = values.seed.trim();
  const windows = walkForward ? whole(values.walkForward.windows, 2, 50) : null;
  const ratio = walkForward ? whole(values.walkForward.trainToTestRatio, 1, 20) : null;

  if (budget === null) {
    errors.budget = 'От 1 до 1 000 000 испытаний.';
  }

  if (finalistCount === null) {
    errors.finalistCount = walkForward ? 'Для проверки на окнах — 0 или 1.' : 'От 0 до 10.';
  }

  if (minExecutions === null) {
    errors.minExecutions = 'Укажите целое неотрицательное число.';
  }

  if (
    maxDrawdown === null ||
    maxDrawdown.startsWith('-') ||
    compareDecimals(maxDrawdown, '100')! > 0
  ) {
    errors.maxDrawdownPercent = 'От 0 до 100%.';
  }

  if (seed !== '' && !/^\d{1,20}$/.test(seed)) {
    errors.seed = 'Целое неотрицательное число или пусто.';
  }

  if (walkForward && windows === null) {
    errors['walkForward.windows'] = 'От 2 до 50 окон.';
  }

  if (walkForward && ratio === null) {
    errors['walkForward.trainToTestRatio'] = 'От 1 до 20.';
  }

  if ('errors' in backtest || Object.keys(errors).length > 0) {
    return {
      errors: { ...errors, ...('errors' in backtest ? { backtest: backtest.errors } : {}) },
    };
  }

  return {
    request: {
      backtest: backtest.request,
      ranges,
      method: values.method,
      objective: values.objective,
      budget: budget!,
      seed: seed === '' ? null : seed,
      finalistCount: finalistCount!,
      minExecutions: minExecutions!,
      maxDrawdownPercent: maxDrawdown!,
      walkForward: walkForward
        ? { windows: windows!, trainToTestRatio: ratio!, scheme: values.walkForward.scheme }
        : null,
    },
  };
}
