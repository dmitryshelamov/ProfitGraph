import { waitingText } from './orderWaiting';
import type { BotOpenOrder, BotOrderWaiting, OrderStatus } from './types';

const statuses: Partial<Record<OrderStatus, string>> = {
  Active: 'Стоит на бирже',
  PendingSubmit: 'Отправляется',
  PendingCancel: 'Отменяется',
};

export function openOrderStatusLabel(status: OrderStatus): string {
  return statuses[status] ?? status;
}

/** The base quantity, or the quote budget of a market buy that has no quantity yet. */
export function openOrderQuantity(order: Pick<BotOpenOrder, 'quantity' | 'quoteBudget'>): string {
  if (order.quantity) {
    return order.quantity;
  }

  return order.quoteBudget ? `на ${order.quoteBudget}` : '—';
}

export function openOrderPrice(order: Pick<BotOpenOrder, 'type' | 'limitPrice'>): string {
  return order.limitPrice ?? (order.type === 'Market' ? 'по рынку' : '—');
}

/**
 * The state of an open order. A working order that its venue or the core holds back (a trade hold, a price band, a closed
 * session) is not on the exchange yet, so it reads «Отложен» with the reason and the time it waits until.
 */
export function openOrderState(
  order: Pick<BotOpenOrder, 'engineOrderId' | 'status'>,
  waiting: readonly Pick<BotOrderWaiting, 'engineOrderId' | 'code' | 'low' | 'high' | 'until'>[],
): { label: string; reason: string | null } {
  const wait =
    order.status === 'Active'
      ? waiting.find((item) => item.engineOrderId === order.engineOrderId)
      : undefined;

  return wait
    ? { label: 'Отложен', reason: waitingText(wait) }
    : { label: openOrderStatusLabel(order.status), reason: null };
}
