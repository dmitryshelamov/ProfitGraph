import { compareDecimals, floorToStep, multiplyDecimals } from '@/shared/lib/decimal';

import type { BotDisplay } from './botDisplay';

/**
 * Why a long position's base cannot be sold: what an order could sell, its total rounded down to the quantity step, is zero,
 * below the minimum quantity or worth less than the minimum order sum at the price.
 */
export type UnsellableRemainder =
  | { readonly kind: 'step'; readonly limit: string }
  | { readonly kind: 'minQuantity'; readonly limit: string }
  | { readonly kind: 'minNotional'; readonly limit: string };

function positive(value: string | null | undefined): value is string {
  return value !== null && value !== undefined && compareDecimals(value, '0') === 1;
}

/**
 * The reason a positive base total is a remainder no order can sell, or null for a sellable position, for no base and while the
 * instrument's rules are unknown. The venue decides for an order the same way, so the engine closed the position's cycle on it.
 */
export function unsellableRemainder(
  baseTotal: string,
  display: BotDisplay,
  price: string | null,
): UnsellableRemainder | null {
  if (compareDecimals(baseTotal, '0') !== 1 || !positive(display.quantityStep)) {
    return null;
  }

  const sellable = floorToStep(baseTotal, display.quantityStep);

  if (sellable === null) {
    return null;
  }

  if (compareDecimals(sellable, '0') !== 1) {
    return { kind: 'step', limit: display.quantityStep };
  }

  if (positive(display.minQuantity) && compareDecimals(sellable, display.minQuantity) === -1) {
    return { kind: 'minQuantity', limit: display.minQuantity };
  }

  const worth = price === null ? null : multiplyDecimals(sellable, price);

  if (
    positive(display.minNotional) &&
    worth !== null &&
    compareDecimals(worth, display.minNotional) === -1
  ) {
    return { kind: 'minNotional', limit: display.minNotional };
  }

  return null;
}

/** Why the remainder cannot be sold, in the card's words. */
export function remainderReason(
  remainder: UnsellableRemainder,
  baseAsset: string | null,
  quoteAsset: string | null,
): string {
  const unit = (asset: string | null) => (asset ? ` ${asset}` : '');

  switch (remainder.kind) {
    case 'step':
      return `меньше шага ${remainder.limit}${unit(baseAsset)}, продать нельзя`;
    case 'minQuantity':
      return `меньше минимального количества ${remainder.limit}${unit(baseAsset)}, продать нельзя`;
    case 'minNotional':
      return `дешевле минимальной суммы заявки ${remainder.limit}${unit(quoteAsset)}, продать нельзя`;
  }
}
