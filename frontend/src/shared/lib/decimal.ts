/**
 * Money, prices, fractions and fees arrive as decimal strings and never pass through JavaScript number.
 * Exact transformations use BigInt; trading arithmetic stays on the backend, except a display value such as a
 * position's unrealized profit at a pushed live price.
 */
const decimalPattern = /^-?(0|[1-9]\d*)(\.\d{1,28})?$/;

export function isDecimalString(value: string): boolean {
  return decimalPattern.test(value);
}

/** The largest coefficient of .NET decimal; a longer value cannot be stored exactly by the API. */
const maxCoefficient = 79228162514264337593543950335n;

/** Converts a percent typed in a form to a stored fraction without floating point: "0.1" → "0.001", "20" → "0.2". */
export function percentToFraction(percent: string): string | null {
  const text = percent.trim();

  if (!isDecimalString(text)) {
    return null;
  }

  const negative = text.startsWith('-');
  const [integer = '0', fraction = ''] = text.replace('-', '').split('.');
  const digits = (integer + fraction).replace(/^0+/, '') || '0';
  const scale = fraction.length + 2;
  const padded = digits.padStart(scale + 1, '0');
  const raw = `${padded.slice(0, padded.length - scale)}.${padded.slice(padded.length - scale)}`;
  const normalized = raw.replace(/^0+(?=\d)/, '').replace(/\.?0+$/, '') || '0';

  return negative && normalized !== '0' ? `-${normalized}` : normalized;
}

/** Whether the API's decimal type can hold the value exactly. */
export function isBackendDecimal(value: string): boolean {
  const parsed = parts(value);

  if (!parsed) {
    return false;
  }

  let coefficient = parsed.coefficient < 0n ? -parsed.coefficient : parsed.coefficient;
  let scale = parsed.scale;

  while (scale > 0 && coefficient % 10n === 0n) {
    coefficient /= 10n;
    scale--;
  }

  return coefficient <= maxCoefficient;
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

/** Rounds a decimal string half away from zero to at most `places` decimals, without floating point: "1.005" → "1.01". */
export function roundDecimal(value: string, places: number): string | null {
  const parsed = parts(value);

  if (!parsed) {
    return null;
  }

  if (parsed.scale <= places) {
    return formatCoefficient(parsed.coefficient, parsed.scale);
  }

  const divisor = 10n ** BigInt(parsed.scale - places);
  const negative = parsed.coefficient < 0n;
  const magnitude = negative ? -parsed.coefficient : parsed.coefficient;
  const rounded = (magnitude + divisor / 2n) / divisor;

  return formatCoefficient(negative ? -rounded : rounded, places);
}

/** Compares two decimal strings exactly: negative when `left` is smaller, zero when equal; null for a non-decimal. */
export function compareDecimals(left: string, right: string): number | null {
  const a = parts(left);
  const b = parts(right);

  if (!a || !b) {
    return null;
  }

  const scale = Math.max(a.scale, b.scale);
  const x = a.coefficient * 10n ** BigInt(scale - a.scale);
  const y = b.coefficient * 10n ** BigInt(scale - b.scale);

  return x < y ? -1 : x > y ? 1 : 0;
}

/** Multiplies two decimal strings exactly; null for a non-decimal. */
export function multiplyDecimals(left: string, right: string): string | null {
  const a = parts(left);
  const b = parts(right);

  return a && b ? formatCoefficient(a.coefficient * b.coefficient, a.scale + b.scale) : null;
}

/** Subtracts `right` from `left` exactly; null for a non-decimal. */
export function subtractDecimals(left: string, right: string): string | null {
  const a = parts(left);
  const b = parts(right);

  if (!a || !b) {
    return null;
  }

  const scale = Math.max(a.scale, b.scale);

  return formatCoefficient(
    a.coefficient * 10n ** BigInt(scale - a.scale) - b.coefficient * 10n ** BigInt(scale - b.scale),
    scale,
  );
}

/** Whether `value` is a whole number of `step`s, exactly; null for a non-decimal or a non-positive step. */
export function isMultipleOf(value: string, step: string): boolean | null {
  const a = parts(value);
  const b = parts(step);

  if (!a || !b || b.coefficient <= 0n) {
    return null;
  }

  const scale = Math.max(a.scale, b.scale);

  return (
    (a.coefficient * 10n ** BigInt(scale - a.scale)) %
      (b.coefficient * 10n ** BigInt(scale - b.scale)) ===
    0n
  );
}

/** Rounds a non-negative `value` down to a whole number of `step`s, as the backend does; null for a non-decimal or a bad step. */
export function floorToStep(value: string, step: string): string | null {
  const a = parts(value);
  const b = parts(step);

  if (!a || !b || b.coefficient <= 0n || a.coefficient < 0n) {
    return null;
  }

  const scale = Math.max(a.scale, b.scale);
  const x = a.coefficient * 10n ** BigInt(scale - a.scale);
  const y = b.coefficient * 10n ** BigInt(scale - b.scale);

  return formatCoefficient((x / y) * y, scale);
}

/** Divides `left` by `right` and rounds the quotient down to `places` decimals; null for a non-decimal or a non-positive `right`. */
export function divideDecimals(left: string, right: string, places: number): string | null {
  const a = parts(left);
  const b = parts(right);

  if (!a || !b || b.coefficient <= 0n || a.coefficient < 0n) {
    return null;
  }

  const numerator = a.coefficient * 10n ** BigInt(places + b.scale);
  const denominator = b.coefficient * 10n ** BigInt(a.scale);

  return formatCoefficient(numerator / denominator, places);
}
