import { rejectionLabel } from './manualOrder';
import type { BotPositionClose } from './types';

/** Whether the close still works: waiting for the bot, cancelling its orders or selling the position. */
export function isActiveClose(close: Pick<BotPositionClose, 'status'>): boolean {
  return close.status === 'Pending' || close.status === 'Cancelling' || close.status === 'Selling';
}

export type CloseStepState = 'done' | 'active' | 'waiting';

export interface CloseStep {
  readonly label: string;
  readonly state: CloseStepState;
}

function step(label: string, at: number, position: number): CloseStep {
  return { label, state: at > position ? 'done' : at === position ? 'active' : 'waiting' };
}

/** The close's steps in order (ADR 0064): the bot takes it, cancels its orders, sells the position and, when asked, stops. */
export function closeSteps(close: BotPositionClose): CloseStep[] {
  const at = { Pending: 0, Cancelling: 1, Selling: 2, Closed: 4, Failed: 4 }[close.status];
  const cancelled = close.cancelledOrders ?? 0;

  return [
    step('Бот принимает запрос', at, 0),
    step(cancelled > 0 ? `Отмена ордеров бота: ${cancelled}` : 'Ордеров для отмены нет', at, 1),
    step('Продажа по рынку', at, 2),
    ...(close.stopAfter ? [step('Остановка бота', at, 3)] : []),
  ];
}

const failureLabels: Readonly<Record<string, string>> = {
  expired: 'бот не принял запрос за минуту',
  position_not_emptied: 'продажа не закрыла позицию',
};

/** Why the close failed, in the owner's words; an engine rejection of the sale is named as for any order. */
export function closeFailureLabel(reason: string | null): string {
  return reason === null ? 'причина неизвестна' : (failureLabels[reason] ?? rejectionLabel(reason));
}

/** What the page says about a close that ended. */
export function closeOutcome(close: BotPositionClose): string {
  if (close.status === 'Failed') {
    return `Закрыть позицию не удалось: ${closeFailureLabel(close.failureReason)}. Бот остановлен.`;
  }

  return close.stopAfter
    ? 'Позиция закрыта, бот остановлен.'
    : 'Позиция закрыта, бот работает дальше.';
}
