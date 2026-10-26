import type { BotOpenOrder, OrderStatus } from './types';

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
