import { fractionToPercent } from '@/shared/lib/decimal';

const readableNames: Record<string, string> = {
  fastPeriod: 'Период быстрой SMA',
  slowPeriod: 'Период медленной SMA',
  smaPeriod: 'Период SMA',
  buyAmount: 'Сумма одной покупки',
  allocationCap: 'Лимит текущей позиции',
  drop: 'Снижение для усреднения',
  rise: 'Рост для продажи',
  impulse: 'Рост свечи для входа',
  volumeMultiple: 'Множитель объёма',
  volumePeriod: 'Период среднего объёма',
  takeProfit: 'Тейк-профит',
  stopLoss: 'Стоп-лосс',
  profitTarget: 'Цель по прибыли',
  maxHold: 'Максимум свечей в позиции',
  interval: 'Интервал покупок в свечах',
};

export function parameterReadableName(name: string): string {
  return Object.hasOwn(readableNames, name) ? readableNames[name]! : name;
}

export type ParameterDisplayUnit = 'percent' | 'quote' | 'base' | 'none';

const displayUnits: readonly string[] = ['percent', 'quote', 'base', 'none'];

/** The unit a parameter is shown in; a missing or unknown unit shows the bare number. */
export function parameterDisplayUnit(displayUnit?: string | null): ParameterDisplayUnit {
  return displayUnit && displayUnits.includes(displayUnit)
    ? (displayUnit as ParameterDisplayUnit)
    : 'none';
}

export function isPercentageParameter(displayUnit?: string | null): boolean {
  return parameterDisplayUnit(displayUnit) === 'percent';
}

/** A parameter marked as percent stores a fraction ("0.015") and is shown as percent ("1.5%"). */
export function parameterValueDisplay(value: string, displayUnit?: string | null): string {
  if (!isPercentageParameter(displayUnit)) {
    return value;
  }

  const percent = fractionToPercent(value);

  return percent === null ? value : `${percent}%`;
}
