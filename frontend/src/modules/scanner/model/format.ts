import { roundDecimal } from '@/shared/lib/decimal';

/** A metric for display: rounded to its places (or exact), thousands grouped; the dash stands for an empty metric. */
export function formatMetric(value: string | null, places: number | null): string {
  if (value === null) {
    return '—';
  }

  const rounded = places === null ? value : (roundDecimal(value, places) ?? value);
  const [whole = '', fraction] = rounded.split('.');
  const sign = whole.startsWith('-') ? '-' : '';
  const digits = sign ? whole.slice(1) : whole;
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

  return fraction === undefined ? `${sign}${grouped}` : `${sign}${grouped}.${fraction}`;
}

/** A value followed by the venue's currency; without a known currency, the fallback (or the value alone). */
export function withQuote(value: string, quote: string, fallback?: string): string {
  return quote ? `${value} ${quote}` : (fallback ?? value);
}
