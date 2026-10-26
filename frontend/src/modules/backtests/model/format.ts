import type { Schemas } from '@/shared/api/client';
import { fractionToPercent, roundDecimal } from '@/shared/lib/decimal';

/** A decimal for reading: rounded to `places`, the exact value kept for a tooltip. Missing values show a dash. */
export function shown(
  value: string | null | undefined,
  places = 2,
): { text: string; exact: string | undefined } {
  if (value === null || value === undefined) {
    return { text: '—', exact: undefined };
  }

  return { text: roundDecimal(value, places) ?? value, exact: value };
}

const outcomes: Record<string, string> = {
  completed: 'Завершён',
  cancelled: 'Отменён',
  failed: 'Ошибка',
};

export function outcomeLabel(outcome: string): string {
  return outcomes[outcome] ?? outcome;
}

/**
 * A run's seller fees in words; the step of seller amounts is named only when it is coarser than one minor unit.
 */
export function sellerFeesLabel(fees: Schemas['SellerFeeScheduleResponse']): string {
  const rates = fees.rates.map((rate) => `${fractionToPercent(rate) ?? rate}%`).join(' + ');
  const step =
    fees.increment === 1 ? '' : `, суммы кратны ${fees.increment} минимальным единицам валюты`;

  return `продавец платит ${rates} с каждой единицы, не меньше ${fees.minimum} за часть${step}`;
}
