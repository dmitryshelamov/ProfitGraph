import { roundDecimal } from '@/shared/lib/decimal';

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
