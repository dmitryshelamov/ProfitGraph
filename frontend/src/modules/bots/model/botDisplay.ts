import { fractionToPercent, percentToFraction, roundDecimal } from '@/shared/lib/decimal';

/** How a bot's amounts are shown: the venue's own decimal places and the traded assets' names. */
export interface BotDisplay {
  readonly basePlaces: number | null;
  readonly quotePlaces: number | null;
  readonly pricePlaces: number | null;
  readonly baseAsset: string | null;
  readonly quoteAsset: string | null;
}

export const unknownBotDisplay: BotDisplay = {
  basePlaces: null,
  quotePlaces: null,
  pricePlaces: null,
  baseAsset: null,
  quoteAsset: null,
};

/** Decimal places of a step or tick: "0.01" → 2, "1" → 0, "0.00010" → 4. */
export function stepPlaces(step: string): number | null {
  if (!/^\d+(\.\d+)?$/.test(step)) {
    return null;
  }

  const fraction = step.split('.')[1]?.replace(/0+$/, '') ?? '';

  return fraction.length;
}

/** The value rounded to the venue's decimal places; unchanged when the places or the value are unknown. */
export function atPlaces(value: string, places: number | null): string {
  return places === null ? value : (roundDecimal(value, places) ?? value);
}

/** An amount with its asset, when the asset is known: "832.758332 DNC". */
export function withAsset(value: string, asset: string | null): string {
  return asset ? `${value} ${asset}` : value;
}

/** A stored fraction as percent: "0.0015" → "0.15%". */
export function fractionLabel(fraction: string): string {
  const percent = fractionToPercent(fraction);

  return percent === null ? fraction : `${percent}%`;
}

/** The text a parameter shows in its input: a percent parameter stores a fraction and is typed as percent. */
export function parameterInputText(stored: string, isPercent: boolean): string {
  return isPercent ? (fractionToPercent(stored) ?? stored) : stored;
}

/** The stored value of a parameter's input text; a text that is not a number is kept as typed for the API to reject. */
export function parameterStoredValue(text: string, isPercent: boolean): string {
  const trimmed = text.trim();

  return isPercent ? (percentToFraction(trimmed) ?? trimmed) : trimmed;
}
