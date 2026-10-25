import type { BotOrderWaiting, OrderWaitingCode } from './types';

const reasons: Record<OrderWaitingCode, string> = {
  PriceBand: 'Ждёт ценового коридора',
  SessionClosed: 'Торги закрыты',
  CarryOver: 'Перенос на следующую торговую сессию',
  SettlementHold: 'Ждёт снятия трейд-холда',
  SingleBuyOrderSlot: 'Ждёт, пока освободится заявка на покупку',
  AwaitingConfirmation: 'Ждёт подтверждения',
  RateLimited: 'Ждёт из-за ограничения запросов',
};

/** Why a venue holds an order locally, with the price band or the end of the hold when known. */
export function waitingText(
  waiting: Pick<BotOrderWaiting, 'code' | 'low' | 'high' | 'until'>,
): string {
  const reason = reasons[waiting.code];

  if (waiting.code === 'PriceBand' && waiting.low && waiting.high) {
    return `${reason} (коридор ${waiting.low}–${waiting.high})`;
  }

  if (waiting.until) {
    return `${reason} до ${new Date(waiting.until).toLocaleString('ru-RU', { timeZone: 'UTC' })} UTC`;
  }

  return reason;
}

export function waitingReason(code: string): string {
  return reasons[code as OrderWaitingCode] ?? code;
}
