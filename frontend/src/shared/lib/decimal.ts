/**
 * Money, prices, fractions and fees arrive as decimal strings and never pass through JavaScript number.
 * Exact transformations use BigInt; trading arithmetic stays on the backend.
 */
const decimalPattern = /^-?(0|[1-9]\d*)(\.\d{1,28})?$/;

export function isDecimalString(value: string): boolean {
  return decimalPattern.test(value);
}

function parts(value: string): { coefficient: bigint; scale: number } | null {
  if (!isDecimalString(value)) {
    return null;
  }

  const negative = value.startsWith('-');
  const [integer = '0', fraction = ''] = value.replace('-', '').split('.');

  return {
    coefficient: BigInt(integer + fraction) * (negative ? -1n : 1n),
    scale: fraction.length,
  };
}

function formatCoefficient(coefficient: bigint, scale: number): string {
  const negative = coefficient < 0n;
  const digits = (negative ? -coefficient : coefficient).toString().padStart(scale + 1, '0');
  const text = scale === 0 ? digits : `${digits.slice(0, -scale)}.${digits.slice(-scale)}`;
  const normalized = scale === 0 ? text : text.replace(/\.?0+$/, '');

  return negative && normalized !== '0' ? `-${normalized}` : normalized;
}

/** Converts a stored fraction to percent without floating point: "0.001" → "0.1", "0.2" → "20". */
export function fractionToPercent(value: string): string | null {
  const parsed = parts(value);

  if (!parsed) {
    return null;
  }

  return parsed.scale >= 2
    ? formatCoefficient(parsed.coefficient, parsed.scale - 2)
    : formatCoefficient(parsed.coefficient * 10n ** BigInt(2 - parsed.scale), 0);
}
