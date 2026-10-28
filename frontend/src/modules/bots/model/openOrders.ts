import { waitingText } from './orderWaiting';
import type { BotOpenOrder, BotOrderWaiting, OrderCancel, OrderStatus } from './types';

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

/**
 * Whether the owner may cancel the order (ADR 0048): one at the venue, held back or still being sent. An order already
 * being cancelled is not offered again.
 */
export function canCancelOpenOrder(order: Pick<BotOpenOrder, 'status'>): boolean {
  return order.status === 'Active' || order.status === 'PendingSubmit';
}

const cancelProblems: Readonly<Record<string, string>> = {
  expired: 'бот не взял отмену за минуту',
  order_not_open: 'ордер уже не активен',
  order_cancel_pending: 'ордер уже отменяется',
  order_cancel_request_pending: 'предыдущая отмена ещё ждёт бота',
  bot_not_running: 'бот не работает',
};

/** The owner-facing reason a cancel was refused or rejected; an unknown code is shown as it is. */
export function cancelProblemLabel(code: string): string {
  return cancelProblems[code] ?? code;
}

/**
 * Where an owner's cancel of `engineOrderId` is: the request waits for the bot, the bot asked the venue and the order is
 * still open, the order is no longer open (cancelled, or filled before the cancel arrived), or the request was rejected.
 */
export function orderCancelState(
  cancel: Pick<OrderCancel, 'status' | 'engineOrderId' | 'rejectionReason'>,
  openOrders: readonly Pick<BotOpenOrder, 'engineOrderId'>[],
): { text: string; settled: boolean } {
  const order = cancel.engineOrderId;

  if (cancel.status === 'Rejected') {
    return {
      text: `Отмена ордера ${order} отклонена: ${cancelProblemLabel(cancel.rejectionReason ?? 'rejected')}.`,
      settled: true,
    };
  }

  if (cancel.status === 'Pending') {
    return { text: `Отмена ордера ${order} ждёт бота…`, settled: false };
  }

  return openOrders.some((item) => item.engineOrderId === order)
    ? { text: `Бот попросил биржу отменить ордер ${order}, ждём подтверждения…`, settled: false }
    : {
        text: `Ордер ${order} больше не активен: биржа его отменила или он успел исполниться (см. ленту).`,
        settled: true,
      };
}
