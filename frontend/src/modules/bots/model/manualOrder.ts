import {
  compareDecimals,
  divideDecimals,
  floorToStep,
  isBackendDecimal,
  isMultipleOf,
  multiplyDecimals,
} from '@/shared/lib/decimal';

import type { OrderSide } from './types';

export type ManualOrderType = 'Market' | 'Limit';

/** The venue's steps and minimums of the bot's symbol, as the instrument rules API returns them. */
export interface ManualOrderRules {
  readonly quantityStep: string;
  readonly priceTick: string;
  readonly minQuantity: string;
  readonly minNotional: string;
  readonly basePrecision: number;
}

export interface ManualOrderInput {
  readonly type: ManualOrderType;
  readonly side: OrderSide;
  readonly amount: string;
  readonly price: string;
}

export interface ManualOrderCheck {
  /** Why the order cannot be sent yet; null when it can. */
  readonly error: string | null;
  /** The base quantity a limit order will rest with, rounded down to the quantity step; null when unknown. */
  readonly quantity: string | null;
}

const noCheck: ManualOrderCheck = { error: null, quantity: null };

/**
 * Checks what the form can know before the runner does (ADR 0046): the amount and price are exact positive decimals and the
 * price lies on the tick. The engine rounds a quantity down to the step and rejects below the minimums, so the form shows the
 * rounded quantity and refuses what would round to nothing; the balance, the risk limits and the venue stay with the backend.
 */
export function checkManualOrder(
  input: ManualOrderInput,
  rules: ManualOrderRules | undefined,
): ManualOrderCheck {
  const amount = input.amount.trim();
  const price = input.price.trim();

  if (!isBackendDecimal(amount) || compareDecimals(amount, '0') !== 1) {
    return { error: amount === '' ? null : 'Сумма — положительное число.', quantity: null };
  }

  if (input.type === 'Market') {
    return noCheck;
  }

  if (price === '') {
    return noCheck;
  }

  if (!isBackendDecimal(price) || compareDecimals(price, '0') !== 1) {
    return { error: 'Цена — положительное число.', quantity: null };
  }

  if (!rules) {
    return noCheck;
  }

  if (isMultipleOf(price, rules.priceTick) === false) {
    return { error: `Цена должна быть кратна шагу цены ${rules.priceTick}.`, quantity: null };
  }

  const wanted = input.side === 'Buy' ? divideDecimals(amount, price, rules.basePrecision) : amount;
  const quantity = wanted === null ? null : floorToStep(wanted, rules.quantityStep);

  if (quantity === null) {
    return noCheck;
  }

  if (compareDecimals(quantity, '0') !== 1) {
    return {
      error: `Количества не хватает на шаг ${rules.quantityStep}: заявка округляется вниз до шага.`,
      quantity,
    };
  }

  if (compareDecimals(quantity, rules.minQuantity) === -1) {
    return { error: `Меньше минимального количества ${rules.minQuantity}.`, quantity };
  }

  const notional = multiplyDecimals(quantity, price);

  if (notional !== null && compareDecimals(notional, rules.minNotional) === -1) {
    return { error: `Меньше минимальной суммы заявки ${rules.minNotional}.`, quantity };
  }

  return { error: null, quantity };
}

const rejectionLabels: Readonly<Record<string, string>> = {
  expired: 'бот не успел взять заявку за минуту',
  TagActive: 'уже стоит другая ручная заявка',
  InsufficientQuote: 'не хватает средств в аллокации бота',
  InsufficientBase: 'не хватает актива',
  InvalidPrice: 'цена после округления до шага равна нулю',
  InvalidAmount: 'неверная сумма',
  ZeroQuantity: 'количество после округления до шага равно нулю',
  BelowMinQuantity: 'меньше минимального количества биржи',
  BelowMinNotional: 'меньше минимальной суммы заявки биржи',
  RiskLimit: 'превышен лимит риска',
  KillSwitch: 'бот остановлен аварийно',
  InstrumentUnavailable: 'инструмент сейчас недоступен',
  BrokerRejected: 'биржа отклонила заявку',
  DuplicateRequest:
    'брокер уже получал заявку с этим ключом, например от копии бота; ничего не выставлено',
};

/** The owner-facing reason of a rejected order; an unknown code is shown as it is. */
export function rejectionLabel(code: string): string {
  return rejectionLabels[code] ?? code;
}
