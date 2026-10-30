import type { ScanRow } from './types';

/** A scanner metric as the API names it (ADR 0059, section 3). */
export type ScanMetric =
  | 'Price'
  | 'Change5m'
  | 'Change15m'
  | 'Change1h'
  | 'Change4h'
  | 'Change24h'
  | 'Turnover1h'
  | 'Turnover24h'
  | 'VolumeSpike'
  | 'VolumeSpike5m'
  | 'VolumeSpike15m'
  | 'Natr24h'
  | 'EfficiencyRatio24h'
  | 'DistanceToHigh24h'
  | 'DistanceToLow24h'
  | 'DistanceToHigh7d'
  | 'DistanceToLow7d'
  | 'Breakout4h'
  | 'Range4h'
  | 'Support'
  | 'DistanceToSupport'
  | 'Resistance'
  | 'DistanceToResistance'
  | 'RewardRisk'
  | 'Spread'
  | 'ListingAgeDays';

export interface MetricDefinition {
  readonly metric: ScanMetric;
  readonly label: string;
  /** Decimal places shown, or null to show the exact value; the value itself stays exact. */
  readonly places: number | null;
  readonly hint: string;
}

export const scanMetrics: readonly MetricDefinition[] = [
  { metric: 'Price', label: 'Цена', places: null, hint: 'Последняя цена' },
  { metric: 'Change5m', label: '5 мин, %', places: 2, hint: 'Изменение за 5 минут' },
  { metric: 'Change15m', label: '15 мин, %', places: 2, hint: 'Изменение за 15 минут' },
  { metric: 'Change1h', label: '1 ч, %', places: 2, hint: 'Изменение за час' },
  { metric: 'Change4h', label: '4 ч, %', places: 2, hint: 'Изменение за 4 часа' },
  { metric: 'Change24h', label: '24 ч, %', places: 2, hint: 'Изменение за сутки' },
  {
    metric: 'Turnover1h',
    label: 'Оборот 1 ч',
    places: 0,
    hint: 'Оборот за час в валюте котировки',
  },
  {
    metric: 'Turnover24h',
    label: 'Оборот 24 ч',
    places: 0,
    hint: 'Оборот за сутки в валюте котировки',
  },
  {
    metric: 'VolumeSpike',
    label: 'Всплеск, ×',
    places: 2,
    hint: 'Оборот последнего часа к медиане часового оборота за неделю',
  },
  {
    metric: 'VolumeSpike5m',
    label: 'Всплеск 5 мин, ×',
    places: 2,
    hint: 'Оборот последних 5 минут к медиане часового оборота за неделю, пересчитанной на 5 минут',
  },
  {
    metric: 'VolumeSpike15m',
    label: 'Всплеск 15 мин, ×',
    places: 2,
    hint: 'Оборот последних 15 минут к медиане часового оборота за неделю, пересчитанной на 15 минут',
  },
  {
    metric: 'Natr24h',
    label: 'Волатильность, %',
    places: 2,
    hint: 'Средний часовой истинный диапазон за сутки к цене',
  },
  {
    metric: 'EfficiencyRatio24h',
    label: 'ER 24 ч',
    places: 2,
    hint: 'Коэффициент эффективности за сутки: 0 — пила, 1 — тренд',
  },
  {
    metric: 'DistanceToHigh24h',
    label: 'До max 24 ч, %',
    places: 2,
    hint: 'Расстояние до максимума суток',
  },
  {
    metric: 'DistanceToLow24h',
    label: 'От min 24 ч, %',
    places: 2,
    hint: 'Расстояние от минимума суток',
  },
  {
    metric: 'DistanceToHigh7d',
    label: 'До max 7 д, %',
    places: 2,
    hint: 'Расстояние до максимума недели',
  },
  {
    metric: 'DistanceToLow7d',
    label: 'От min 7 д, %',
    places: 2,
    hint: 'Расстояние от минимума недели',
  },
  {
    metric: 'Breakout4h',
    label: 'Пробой 4 ч, %',
    places: 2,
    hint: 'Цена к максимуму 4 часов до последних 15 минут: выше нуля — вышла из диапазона',
  },
  {
    metric: 'Range4h',
    label: 'Диапазон 4 ч, %',
    places: 2,
    hint: 'Ширина диапазона 4 часов до последних 15 минут: узкий — сжатие перед пробоем',
  },
  {
    metric: 'Support',
    label: 'Поддержка',
    places: null,
    hint: 'Ближайший уровень ниже цены: часовые минимумы-развороты и минимум недели',
  },
  {
    metric: 'DistanceToSupport',
    label: 'До подд., %',
    places: 2,
    hint: 'Расстояние от цены до поддержки',
  },
  {
    metric: 'Resistance',
    label: 'Сопротивление',
    places: null,
    hint: 'Ближайший уровень выше цены: часовые максимумы-развороты и максимум недели',
  },
  {
    metric: 'DistanceToResistance',
    label: 'До сопр., %',
    places: 2,
    hint: 'Расстояние от цены до сопротивления',
  },
  {
    metric: 'RewardRisk',
    label: 'R:R',
    places: 2,
    hint: 'Расстояние до сопротивления к расстоянию до поддержки: прибыль к риску лонга от цены',
  },
  { metric: 'Spread', label: 'Спред, %', places: 3, hint: 'Разница лучших цен к середине' },
  {
    metric: 'ListingAgeDays',
    label: 'Возраст, дн',
    places: 0,
    hint: 'Дней с листинга; известно только для пар младше недели',
  },
];

const fields: Record<ScanMetric, keyof ScanRow> = {
  Price: 'price',
  Change5m: 'change5m',
  Change15m: 'change15m',
  Change1h: 'change1h',
  Change4h: 'change4h',
  Change24h: 'change24h',
  Turnover1h: 'turnover1h',
  Turnover24h: 'turnover24h',
  VolumeSpike: 'volumeSpike',
  VolumeSpike5m: 'volumeSpike5m',
  VolumeSpike15m: 'volumeSpike15m',
  Natr24h: 'natr24h',
  EfficiencyRatio24h: 'efficiencyRatio24h',
  DistanceToHigh24h: 'distanceToHigh24h',
  DistanceToLow24h: 'distanceToLow24h',
  DistanceToHigh7d: 'distanceToHigh7d',
  DistanceToLow7d: 'distanceToLow7d',
  Breakout4h: 'breakout4h',
  Range4h: 'range4h',
  Support: 'support',
  DistanceToSupport: 'distanceToSupport',
  Resistance: 'resistance',
  DistanceToResistance: 'distanceToResistance',
  RewardRisk: 'rewardRisk',
  Spread: 'spread',
  ListingAgeDays: 'listingAgeDays',
};

/** A row's metric as an exact decimal string, or null when the pair lacks the history. */
export function metricValue(row: ScanRow, metric: ScanMetric): string | null {
  const value = row[fields[metric]];

  return value === null || value === undefined ? null : String(value);
}

export function metricLabel(metric: string): string {
  return scanMetrics.find((definition) => definition.metric === metric)?.label ?? metric;
}
